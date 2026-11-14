using System.Net;
using System.Net.Http.Json;
using MockBucket.Core;
using Xunit;

namespace MockBucket.Tests;

/// <summary>
/// Rules acceptance: stored per-bucket rules — first enabled match wins, mb_* overrides
/// outrank them, CRUD over the public API, glob/query/header matching.
/// </summary>
public class RuleTests
{
    private static async Task<(Probe F, string Id)> NewBucket()
    {
        var f = new Probe();
        var res = await f.Client.PostAsJsonAsync("/api/buckets", new { name = "rules" });
        var b = await res.Content.ReadFromJsonAsync<Json.BucketDto>();
        return (f, b!.Id);
    }

    private static async Task PutRules(Probe f, string id, params Rule[] rules)
    {
        var res = await f.Client.PutAsJsonAsync($"/api/buckets/{id}/rules", rules.ToList());
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Matching_rule_answers_the_caller()
    {
        var (f, id) = await NewBucket();
        await PutRules(f, id, new Rule
        {
            Name = "teapot",
            Match = new RuleMatch { Method = "POST", Path = "/brew/*" },
            Respond = new RuleResponse { Status = 418, Body = "no coffee here", ContentType = "text/plain" },
        });

        var hit = await f.Client.PostAsync($"/b/{id}/brew/tea", null);
        Assert.Equal((HttpStatusCode)418, hit.StatusCode);
        Assert.Equal("no coffee here", await hit.Content.ReadAsStringAsync());

        var miss = await f.Client.PostAsync($"/b/{id}/grind", null); // path unmatched → default
        Assert.Equal(HttpStatusCode.OK, miss.StatusCode);

        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal(["rule", "default"], entries!.Entries.Select(e => e.Source).ToArray());
    }

    [Fact]
    public async Task First_enabled_match_wins()
    {
        var (f, id) = await NewBucket();
        await PutRules(f, id,
            new Rule { Enabled = false, Match = new RuleMatch { Path = "/*" }, Respond = new RuleResponse { Status = 503, Body = "disabled" } },
            new Rule { Match = new RuleMatch { Path = "/a*" }, Respond = new RuleResponse { Status = 201, Body = "first" } },
            new Rule { Match = new RuleMatch { Path = "/*" }, Respond = new RuleResponse { Status = 202, Body = "second" } });

        var res = await f.Client.GetAsync($"/b/{id}/apple");
        Assert.Equal((HttpStatusCode)201, res.StatusCode);
        Assert.Equal("first", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_and_header_conditions_narrow_matches()
    {
        var (f, id) = await NewBucket();
        await PutRules(f, id, new Rule
        {
            Match = new RuleMatch
            {
                Path = "/data",
                Query = new() { ["token"] = "s3cret" },
                Headers = new() { ["X-Flags"] = "*" },           // must be present, any value
            },
            Respond = new RuleResponse { Status = 200, Body = "ok", ContentType = "application/json" },
        });

        var req = new HttpRequestMessage(HttpMethod.Get, $"/b/{id}/data?token=s3cret&other=1");
        req.Headers.Add("X-Flags", "whatever");
        Assert.Equal("ok", await (await f.Client.SendAsync(req)).Content.ReadAsStringAsync());

        var noToken1 = new HttpRequestMessage(HttpMethod.Get, $"/b/{id}/data?token=wrong");
        noToken1.Headers.Add("X-Flags", "x");
        Assert.Equal(HttpStatusCode.OK, (await f.Client.SendAsync(noToken1)).StatusCode); // default, body empty
        Assert.Empty(await (await f.Client.GetAsync($"/b/{id}/data?token=wrong")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Overrides_outrank_rules()
    {
        var (f, id) = await NewBucket();
        await PutRules(f, id, new Rule
        {
            Match = new RuleMatch { Path = "/*" },
            Respond = new RuleResponse { Status = 500, Body = "rule says boom" },
        });

        var res = await f.Client.GetAsync($"/b/{id}/x?mb_status=204");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode); // caller's intent beats stored config
        var entries = await (await f.Client.GetAsync($"/api/buckets/{id}/entries")).Content.ReadFromJsonAsync<Json.Envelopes>();
        Assert.Equal("override", entries!.Entries[0].Source);
    }

    [Fact]
    public async Task Rules_survive_roundtrip_and_delete()
    {
        var (f, id) = await NewBucket();
        var created = await f.Client.PostAsJsonAsync($"/api/buckets/{id}/rules",
            new Rule { Name = "one", Match = new RuleMatch { Method = "GET" }, Respond = new RuleResponse { Status = 203 } });
        created.EnsureSuccessStatusCode();
        var rule = await created.Content.ReadFromJsonAsync<Rule>();

        var listed = await f.Client.GetFromJsonAsync<RulesDoc>($"/api/buckets/{id}/rules");
        Assert.Single(listed.Rules);

        Assert.Equal(HttpStatusCode.NoContent, (await f.Client.DeleteAsync($"/api/buckets/{id}/rules/{rule!.Id}")).StatusCode);
        var after = await f.Client.GetFromJsonAsync<RulesDoc>($"/api/buckets/{id}/rules");
        Assert.Empty(after!.Rules);
    }

    private sealed record RulesDoc(List<Rule> Rules);

    [Fact]
    public async Task Regex_path_conditions_work()
    {
        var (f, id) = await NewBucket();
        await PutRules(f, id, new Rule
        {
            Match = new RuleMatch { Path = @"~^/v\d+/health$" },
            Respond = new RuleResponse { Status = 200, Body = """{"alive":true}""", ContentType = "application/json" },
        });
        Assert.Equal("""{"alive":true}""", await (await f.Client.GetAsync($"/b/{id}/v2/health")).Content.ReadAsStringAsync());
        Assert.Equal("", await (await f.Client.GetAsync($"/b/{id}/vX/health")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var f = new Probe();
        var doc = await f.Client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/buckets", doc);
        Assert.Matches(@"/b/\{", doc); // the capture route is in the contract, whatever the param casing
    }
}
