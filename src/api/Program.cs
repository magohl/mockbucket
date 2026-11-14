using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using MockBucket.Core;

var builder = WebApplication.CreateBuilder(args);

// Auth lives on the hosting side — our job is to be proxy-friendly.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var options = CaptureOptions.FromEnvironment();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<BucketRegistry>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<CaptureService>();
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(); // contract published at /openapi/v1.json

var app = builder.Build();

app.MapOpenApi();

app.UseForwardedHeaders();
app.UseDefaultFiles(); // serve the SPA from wwwroot
app.UseStaticFiles();

var json = EntryJson.Options;

// ---- ops surface ----------------------------------------------------------
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/info", (BucketRegistry buckets) => Results.Ok(new
{
    name = "MockBucket",
    buckets = buckets.Count,
    autoCreate = options.AutoCreate,
    retention = new { maxEntries = options.MaxEntries, idleHours = options.IdleExpiryHours, bodyCapBytes = options.BodyCapBytes },
}));

// ---- management API ------------------------------------------------
app.MapPost("/api/buckets", (BucketRegistry buckets, CreateBucketRequest? req) =>
{
    var b = buckets.Create(req?.Name);
    return Results.Ok(new { id = b.Id, name = b.Name, created = b.Created, urls = new { capture = $"/b/{b.Id}", ui = $"/buckets/{b.Id}" } });
});

app.MapGet("/api/buckets", (BucketRegistry buckets) => Results.Ok(new { buckets = buckets.List() }));

app.MapGet("/api/buckets/{id}", (BucketRegistry buckets, string id) =>
    buckets.Get(id) is { } b ? Results.Ok(b.Info) : Results.NotFound());

app.MapDelete("/api/buckets/{id}", (BucketRegistry buckets, EventHub hub, string id) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    buckets.Remove(id);
    hub.Publish(id, "gone", "{}");
    return Results.NoContent();
});

app.MapDelete("/api/buckets/{id}/entries", (BucketRegistry buckets, EventHub hub, string id) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    b.Entries.Clear();
    hub.Publish(id, "purge", "{}"); // clients reset their cursor; ring evictions are handled by seq-gap resync
    return Results.NoContent();
});

app.MapGet("/api/buckets/{id}/entries", (BucketRegistry buckets, string id, long? since, int? take) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    var items = b.Entries.Snapshot().Where(e => e.Seq > (since ?? 0));
    if (take is > 0) items = items.Take(take.Value);
    return Results.Ok(new { entries = items.Select(e => e.Summarize()).ToArray() });
});

// ---- entry read endpoints ----------------------------------------------------
app.MapGet("/api/entries/{id}", (BucketRegistry buckets, string id) =>
    Resolve(buckets, id) is { } e ? Results.Ok(e.Detail()) : Results.NotFound());

app.MapGet("/api/entries/{id}/raw", (BucketRegistry buckets, string id) =>
    Resolve(buckets, id) is { } e
        ? Results.Bytes(CaptureService.RawRequest(e), "application/octet-stream")
        : Results.NotFound());

app.MapGet("/api/entries/{id}/body", (BucketRegistry buckets, string id) =>
    Resolve(buckets, id) is { } e
        ? Results.Bytes(e.BodyBytes ?? [], "application/octet-stream")
        : Results.NotFound());

// ---- rules ----------------------------------------
app.MapGet("/api/buckets/{id}/rules", (BucketRegistry buckets, string id) =>
    buckets.Get(id) is { } b ? Results.Ok(new { rules = Snapshot(b) }) : Results.NotFound());

app.MapPut("/api/buckets/{id}/rules", (BucketRegistry buckets, EventHub hub, string id, List<Rule>? rules) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    lock (b.Rules) { b.Rules.Clear(); b.Rules.AddRange(rules ?? []); }
    hub.Publish(id, "rules", "{}");
    return Results.Ok(new { rules = Snapshot(b) });
});

app.MapPost("/api/buckets/{id}/rules", (BucketRegistry buckets, EventHub hub, string id, Rule? rule) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    if (rule is null) return Results.BadRequest();
    lock (b.Rules) b.Rules.Add(rule);
    hub.Publish(id, "rules", "{}");
    return Results.Ok(rule);
});

app.MapDelete("/api/buckets/{id}/rules/{ruleId}", (BucketRegistry buckets, EventHub hub, string id, string ruleId) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    lock (b.Rules) b.Rules.RemoveAll(r => r.Id == ruleId);
    hub.Publish(id, "rules", "{}");
    return Results.NoContent();
});

// ---- SSE live feed ---------------------------------------------
app.MapGet("/api/buckets/{id}/stream", async (HttpContext ctx, BucketRegistry buckets, EventHub hub, string id) =>
{
    if (buckets.Get(id) is not { } b) return Results.NotFound();
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // no proxy buffering
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

    var sub = hub.Subscribe(id);
    try
    {
        await ctx.Response.WriteAsync(": open\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var wait = sub.Channel.Reader.WaitToReadAsync(ctx.RequestAborted).AsTask();
            var beat = Task.Delay(TimeSpan.FromSeconds(15), ctx.RequestAborted);
            if (await Task.WhenAny(wait, beat) == wait)
            {
                if (!await wait) break;
                while (sub.Channel.Reader.TryRead(out var ev))
                {
                    await ctx.Response.WriteAsync($"event: {ev.Name}\ndata: {ev.Data}\n\n", ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            else await ctx.Response.WriteAsync(": ping\n\n", ctx.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    finally { hub.Unsubscribe(id, sub); }
    return Results.Empty;
});

// ---- capture: ANY /b/{id}/** ------------------------------------------
app.MapMethods("/b/{id}/{**rest}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"], // verbs for OpenAPI; any verb is captured
    async (HttpContext ctx, BucketRegistry buckets, CaptureService capture, CaptureOptions opts, string id, string? rest) =>
{
    var (idPart, urlStatus, specError) = BucketRegistry.ParseSpec(id);
    if (specError is not null)
    {
        // a bad instruction for an existing bucket; unknown bucket stays a plain 404
        if (buckets.Get(idPart) is not { } target) { ctx.Response.StatusCode = 404; return; }
        var bad = await capture.RecordAsync(ctx, target, rest);
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://mockbucket/bad-url-config",
            title = "Invalid bucket URL",
            errors = new[] { specError },
        }, json));
        bad.ResponseStatus = 400;
        capture.Commit(target, bad, "error");
        return;
    }

    // suffix form never mints buckets; plain ids keep the auto-create policy
    var bucket = urlStatus is null ? buckets.GetOrCreate(idPart, opts.AutoCreate) : buckets.Get(idPart);
    if (bucket is null) { ctx.Response.StatusCode = 404; return; }

    var entry = await capture.RecordAsync(ctx, bucket, rest);

    string source;
    try
    {
        var o = Overrides.Present(ctx.Request.Query) ? Overrides.Parse(ctx.Request.Query) : null;
        var rule = o is null && urlStatus is null
            ? RuleEngine.FirstMatch(Snapshot(bucket), entry.Method, entry.Path, entry.Query, entry.Headers)
            : null;
        source = await capture.ApplyResponseAsync(ctx, o, rule, urlStatus);
    }
    catch (OverrideException ex)
    {
        // clean 400s for malformed controls, and the attempt is still recorded (it did arrive)
        source = "error";
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://mockbucket/bad-request-control",
            title = "Invalid mb_* override",
            errors = ex.Errors,
        }, json));
    }

    entry.ResponseStatus = ctx.Response.StatusCode;
    capture.Commit(bucket, entry, source);
});

app.MapFallbackToFile("index.html"); // SPA client routes (the /b/** capture route wins — it is mapped first)

app.Run();

// ---- helpers ---------------------------------------------------------------
static IReadOnlyList<Rule> Snapshot(Bucket b) { lock (b.Rules) return b.Rules.ToArray(); }

static CaptureEntry? Resolve(BucketRegistry buckets, string id)
{
    var i = id.LastIndexOf('_');
    if (i <= 0 || !long.TryParse(id[(i + 1)..], out var seq)) return null;
    return buckets.Get(id[..i])?.Entries.Snapshot().FirstOrDefault(e => e.Seq == seq);
}

public partial class Program; // marker for WebApplicationFactory in tests
