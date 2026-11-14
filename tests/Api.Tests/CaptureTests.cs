using System.Net;
using System.Net.Http.Json;
using MockBucket.Core;
using Xunit;

namespace MockBucket.Tests;

/// <summary>
/// Capture acceptance: records method/path/query/headers/body, ring + cap behave,
/// mb_* overrides answer the caller, the auto-create policy holds, SSE streams.
/// </summary>
public class CaptureTests
{
    private static async Task<(Probe F, string Id)> NewBucket(Func<CaptureOptions, CaptureOptions>? tweak = null)
    {
        var f = new Probe { Tweak = tweak };
        var res = await f.Client.PostAsJsonAsync("/api/buckets", new { name = "t" });
        var b = await res.Content.ReadFromJsonAsync<Json.BucketDto>();
        return (f, b!.Id);
    }

    // ---- recording ----------------------------------------------------

    [Fact]
    public async Task Capture_records_method_path_query_headers_and_body()
    {
        var (f, id) = await NewBucket();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/b/{id}/orders/42?x=1&y=two&y=2");
        req.Headers.Add("X-Test", "hello");
        req.Content = new StringContent("""{"note":"captured"}""", null, "application/json");

        var res = await f.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // no rules, no overrides → 200 default

        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        var summary = Assert.Single(entries!.Entries);
        Assert.Equal("POST", summary.Method);
        Assert.Equal("/orders/42", summary.Path);

        var detail = await (await f.Client.GetAsync($"/api/entries/{summary.Id}")).Content.ReadFromJsonAsync<Json.EntryDetail>();
        Assert.Equal("hello", detail!.Headers["X-Test"][0]);
        Assert.Equal(new[] { "1" }, detail.Query["x"]);
        Assert.Equal(new[] { "two", "2" }, detail.Query["y"]);
        Assert.Equal("json", detail.Body.Kind);
        Assert.Contains("captured", detail.Body.Text);
        Assert.Equal("default", detail.Response.Source);
    }

    [Fact]
    public async Task Since_cursor_pages_incrementally()
    {
        var (f, id) = await NewBucket();
        await f.Client.PostAsync($"/b/{id}/a", null);
        var first = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        await f.Client.PostAsync($"/b/{id}/b", null);
        var next = await (await f.Client.GetAsync($"/api/buckets/{id}/entries?since={first!.Entries[0].Seq}"))
            .Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Single(next!.Entries);
        Assert.Equal("/b", next.Entries[0].Path);
    }

    // ---- retention ---------------------------------------------------------

    [Fact]
    public async Task Ring_buffer_evicts_oldest_at_capacity()
    {
        var (f, id) = await NewBucket(o => o with { MaxEntries = 3 });
        for (var i = 0; i < 5; i++) await f.Client.PostAsync($"/b/{id}/p{i}", null);
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal(3, entries!.Entries.Length);
        Assert.Equal("/p2", entries.Entries[0].Path); // p0, p1 evicted
    }

    [Fact]
    public async Task Body_is_capped_with_truncation_flag()
    {
        var (f, id) = await NewBucket(o => o with { BodyCapBytes = 1024 });
        var big = new string('a', 2000);
        await f.Client.PostAsync($"/b/{id}", new StringContent(big));
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        var detail = await (await f.Client.GetAsync($"/api/entries/{entries!.Entries[0].Id}")).Content.ReadFromJsonAsync<Json.EntryDetail>();
        Assert.True(detail!.Body.Truncated);
        Assert.Equal(big.Length, detail.Body.Bytes);   // true length recorded
        Assert.Equal(1024, detail.Body.Text!.Length);  // stored capped
    }

    [Fact]
    public async Task Binary_body_is_flagged_not_text()
    {
        var (f, id) = await NewBucket();
        await f.Client.PostAsync($"/b/{id}", new ByteArrayContent([0x00, 0x01, 0xFE, 0xFF]));
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        var detail = await (await f.Client.GetAsync($"/api/entries/{entries!.Entries[0].Id}")).Content.ReadFromJsonAsync<Json.EntryDetail>();
        Assert.Equal("binary", detail!.Body.Kind);
        Assert.Null(detail.Body.Text);
        var raw = await f.Client.GetByteArrayAsync($"/api/entries/{entries.Entries[0].Id}/raw");
        Assert.Contains((byte)0xFE, raw); // body survives into the raw download
    }

    // ---- URL config (.status suffix) ---------------

    [Fact]
    public async Task Url_suffix_answers_without_rules()
    {
        var (f, id) = await NewBucket();
        var res = await f.Client.PostAsync($"/b/{id}.418/anything?a=true", null);
        Assert.Equal((HttpStatusCode)418, res.StatusCode);
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        var summary = Assert.Single(entries!.Entries);
        Assert.Equal("url", summary.Source);
        Assert.Equal("/anything", summary.Path); // suffix lives in the id segment, path stays the app's
    }

    [Fact]
    public async Task Url_suffix_beats_rules_and_mb_query_beats_suffix()
    {
        var (f, id) = await NewBucket();
        await f.Client.PutAsJsonAsync($"/api/buckets/{id}/rules",
            new[] { new { name = "all200", enabled = true, match = new { path = "/*" }, respond = new { status = 200 } } });
        Assert.Equal((HttpStatusCode)503, (await f.Client.GetAsync($"/b/{id}.503/x")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client.GetAsync($"/b/{id}.503/x?mb_status=204")).StatusCode);
    }

    [Fact]
    public async Task Malformed_suffix_is_clean_400_and_recorded_as_error()
    {
        var (f, id) = await NewBucket();
        var res = await f.Client.GetAsync($"/b/{id}.99/x");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal("error", Assert.Single(entries!.Entries).Source);
    }

    [Fact]
    public async Task Suffix_never_mints_buckets_even_with_autocreate_on()
    {
        var f = new Probe { Tweak = o => o with { AutoCreate = true } };
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.PostAsync("/b/ABCDEF.418/x", null)).StatusCode);
    }

    [Fact]
    public async Task Suffix_survives_lowercase_and_Crockford_typos()
    {
        var (f, id) = await NewBucket();
        Assert.Equal((HttpStatusCode)404, (await f.Client.GetAsync($"/b/{id.ToLowerInvariant()}.4O4/x")).StatusCode); // O→0 → ".404"
    }

    // ---- auto-create policy -------------------------------------------------


    [Fact]
    public async Task Unknown_bucket_is_404_with_autocreate_off()
    {
        var (f, _) = await NewBucket();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync("/b/zzzzzzzzzz/anything")).StatusCode);
    }

    [Fact]
    public async Task Autocreate_on_mints_valid_shaped_ids_only()
    {
        var f = new Probe { Tweak = o => o with { AutoCreate = true } };
        Assert.Equal(HttpStatusCode.OK, (await f.Client.PostAsync("/b/ABCDEF/x", null)).StatusCode); // Crockford ×6
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.PostAsync("/b/abcdefghij/x", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.PostAsync("/b/tooshort/x", null)).StatusCode); // internet probes bounce
    }

    // ---- mb_* overrides --------------------------------------

    [Fact]
    public async Task Overrides_shape_the_response()
    {
        var (f, id) = await NewBucket();
        var url = $"/b/{id}/x?mb_status=401" +
                  $"&mb_header={Uri.EscapeDataString("X-Deny: later")}" +
                  $"&mb_body={Uri.EscapeDataString("""{"no":"peas"}""")}&mb_type=application/json";
        var res = await f.Client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("later", res.Headers.GetValues("X-Deny").Single());
        Assert.Equal("""{"no":"peas"}""", await res.Content.ReadAsStringAsync());

        // recorded with source=override — readable without base64 archaeology
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal("override", entries!.Entries[0].Source);
        Assert.Equal(401, entries.Entries[0].Status);
    }

    [Fact]
    public async Task Malformed_override_gets_clean_400_and_is_recorded()
    {
        var (f, id) = await NewBucket();
        var res = await f.Client.GetAsync($"/b/{id}/x?mb_status=999&mb_nope=1");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadAsStringAsync();
        Assert.Contains("mb_status", problem);
        Assert.Contains("mb_nope", problem);
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal("error", entries!.Entries[0].Source); // the attempt did arrive — we show it
    }

    // ---- SSE ------------------------------------------------------------------

    [Fact]
    public async Task Sse_stream_delivers_entry_events_live()
    {
        var (f, id) = await NewBucket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = await f.Client.GetAsync($"/api/buckets/{id}/stream", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        stream.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));

        var post = await f.Client.PostAsync($"/b/{id}/live", null); // separate request, same in-memory host
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var lines = new List<string>();
        while (lines.Count < 3) // ": open" + "event: entry" + "data: …"
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null) break;
            if (line.Length > 0) lines.Add(line);
        }
        Assert.Contains("event: entry", lines);
        Assert.Contains(lines, l => l.StartsWith("data:") && l.Contains("\"/live\""));
    }

    [Fact]
    public async Task Entries_can_be_purged()
    {
        var (f, id) = await NewBucket();
        await f.Client.PostAsync($"/b/{id}/x", null);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client.DeleteAsync($"/api/buckets/{id}/entries")).StatusCode);
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Empty(entries!.Entries);
    }
}

/// <summary>Wire DTOs mirroring the public JSON contract.</summary>
internal static class Json
{
    internal sealed record BucketDto(string Id, string? Name, DateTimeOffset Created, UrlsDto? Urls);
    internal sealed record UrlsDto(string Capture, string Ui);
    internal sealed record Envelopes(EntryOut[] Entries);
    internal sealed record EntryOut(string Id, long Seq, DateTimeOffset Time, string Method, string Path, int Status, string Source, long BodyBytes, string BodyKind);
    internal sealed record EntryDetail(string Id, Dictionary<string, string?[]> Query, Dictionary<string, string?[]> Headers, BodyOut Body, ResponseOut Response);
    internal sealed record BodyOut(string Kind, long Bytes, bool Truncated, string? Text);
    internal sealed record ResponseOut(int Status, string Source);
}
