using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MockBucket.Tests;

/// <summary>Contract checks for the basics — kept alive so refactors don't break them.</summary>
public class ScaffoldSmoke : IClassFixture<Probe>
{
    private readonly Probe _factory;
    public ScaffoldSmoke(Probe factory) => _factory = factory;

    [Fact]
    public async Task Healthz_is_ok()
    {
        var res = await _factory.Client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Bucket_create_returns_a_typeable_id()
    {
        var res = await _factory.Client.PostAsJsonAsync("/api/buckets", new { name = "smoke" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<Json.BucketDto>();
        Assert.Matches("^[0-9A-Z]{6}$", body!.Id); // Crockford base32: uppercase, no I/L/O/U
    }

    [Fact]
    public async Task Id_lookup_tolerates_case_and_hyphens()
    {
        var res = await _factory.Client.PostAsJsonAsync("/api/buckets", new { name = "casey" });
        var body = await res.Content.ReadFromJsonAsync<Json.BucketDto>();
        var awkward = body!.Id.ToLowerInvariant().Insert(3, "-"); // e.g. k7t-r9m
        var lookup = await _factory.Client.GetAsync($"/api/buckets/{awkward}");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
    }
}
