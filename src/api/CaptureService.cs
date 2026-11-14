using System.Text;
using System.Text.Json;

namespace MockBucket.Core;

/// <summary>
/// Capture pipeline: record what arrived, decide what goes back.
/// Response precedence: mb_* override (the caller's explicit intent) > stored rule
/// (first enabled match) > default 200. The entry records the final verdict either way.
/// </summary>
public sealed class CaptureService(CaptureOptions options, EventHub hub)
{
    /// <summary>Reads the request into a draft entry (body capped + sniffed).</summary>
    public async Task<CaptureEntry> RecordAsync(HttpContext ctx, Bucket bucket, string? rest)
    {
        var (body, len, truncated) = await ReadBodyAsync(ctx.Request, options.BodyCapBytes);
        var kind = Sniff(body, ctx.Request.ContentType);
        var seq = Interlocked.Increment(ref bucket.NextSeq);

        var entry = new CaptureEntry
        {
            Id = $"{bucket.Id}_{seq}",
            BucketId = bucket.Id,
            Seq = seq,
            Time = DateTimeOffset.UtcNow,
            Method = ctx.Request.Method.ToUpperInvariant(),
            RawPath = ctx.Request.Path,
            Path = "/" + (rest ?? "").TrimStart('/'),
            Query = ctx.Request.Query.ToDictionary(
                kv => kv.Key, kv => (string?[])kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            Headers = ctx.Request.Headers.ToDictionary(
                kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
            BodyBytes = body,
            BodyLength = len,
            BodyTruncated = truncated,
            BodyKind = kind,
            BodyText = kind is "text" or "json" && body is not null ? Encoding.UTF8.GetString(body) : null,
        };

        bucket.LastSeen = entry.Time;
        return entry;
    }

    /// <summary>Decides + writes the HTTP response; returns the source tag for the record.</summary>
    public async Task<string> ApplyResponseAsync(HttpContext ctx, OverrideResponse? o, Rule? rule, int? urlStatus = null)
    {
        if (o is not null)
        {
            ctx.Response.StatusCode = o.Status;
            foreach (var (k, v) in o.Headers) ctx.Response.Headers[k] = v;
            await WriteBodyAsync(ctx, o.Body, o.ContentType);
            return "override";
        }
        if (urlStatus is { } us) // URL config beats standing rules, loses to mb_* query
        {
            ctx.Response.StatusCode = us;
            return "url";
        }
        if (rule is not null)
        {
            ctx.Response.StatusCode = rule.Respond.Status;
            foreach (var (k, v) in rule.Respond.Headers ?? []) ctx.Response.Headers[k] = v;
            await WriteBodyAsync(ctx, rule.Respond.Body, rule.Respond.ContentType);
            return "rule";
        }
        ctx.Response.StatusCode = 200;
        return "default";
    }

    private static async Task WriteBodyAsync(HttpContext ctx, string? body, string? contentType)
    {
        if (body is null) return;
        ctx.Response.ContentType = contentType ?? "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync(body);
    }

    /// <summary>Stores the entry in the ring and fans out to SSE subscribers (after the response is decided).</summary>
    public void Commit(Bucket bucket, CaptureEntry entry, string source)
    {
        entry.ResponseSource = source;
        bucket.Entries.Add(entry);
        hub.Publish(bucket.Id, "entry", JsonSerializer.Serialize(entry.Summarize(), EntryJson.Options));
    }

    // ---- plumbing -----------------------------------------------------------

    private static async Task<(byte[]? body, long length, bool truncated)> ReadBodyAsync(HttpRequest req, int cap)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0; bool truncated = false;
        int read;
        while ((read = await req.Body.ReadAsync(buffer)) > 0)
        {
            total += read;
            var room = Math.Min(read, cap - (int)ms.Length);
            if (room > 0) ms.Write(buffer, 0, room);
            if (room < read) truncated = true;
        }
        return total == 0 ? (null, 0, false) : (ms.ToArray(), total, truncated);
    }

    /// <summary>utf-8 vs binary vs json sniff.</summary>
    internal static string Sniff(byte[]? body, string? contentType)
    {
        if (body is null || body.Length == 0) return "none";
        bool isText;
        try
        {
            var s = Encoding.UTF8.GetString(body); // strict: throws on invalid utf-8
            isText = !s.AsSpan(0, Math.Min(s.Length, 512)).Contains('\0');
        }
        catch (ArgumentException) { isText = false; }

        if (!isText) return "binary";
        var trimmed = Encoding.UTF8.GetString(body).AsSpan().TrimStart();
        var jsonish = trimmed.Length > 0 && trimmed[0] is '{' or '[';
        if (!jsonish) return "text";
        if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) is true) return "json";
        try { JsonDocument.Parse(Encoding.UTF8.GetString(body)).Dispose(); return "json"; }
        catch (JsonException) { return "text"; }
    }

    /// <summary>Best-effort HTTP wire form of a stored entry for the raw download.</summary>
    public static byte[] RawRequest(CaptureEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Method).Append(' ').Append(e.RawPath).Append(" HTTP/1.1\r\n");
        foreach (var (k, vs) in e.Headers)
            foreach (var v in vs) sb.Append(k).Append(": ").Append(v).Append('\r');
        sb.Append('\n');
        var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var body = e.BodyBytes ?? [];
        return [.. headerBytes, .. body];
    }
}
