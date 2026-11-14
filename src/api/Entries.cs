using System.Text.Json;

namespace MockBucket.Core;

/// <summary>One recorded request. Bodies capped at capture options' limit, truncation flagged.</summary>
public sealed class CaptureEntry
{
    public required string Id { get; init; }          // "{bucketId}_{seq}" — decodable, no index needed
    public required string BucketId { get; init; }
    public long Seq { get; init; }
    public DateTimeOffset Time { get; init; }
    public required string Method { get; init; }
    public required string RawPath { get; init; }     // as received, e.g. /b/abc123/foo/bar
    public required string Path { get; init; }        // after the bucket prefix, e.g. /foo/bar
    public required Dictionary<string, string?[]> Query { get; init; }
    public required Dictionary<string, string?[]> Headers { get; init; } // case-insensitive
    public string? ClientIp { get; init; }
    public byte[]? BodyBytes { get; init; }
    public long BodyLength { get; init; }             // full size, pre-cap
    public bool BodyTruncated { get; init; }
    public string BodyKind { get; init; } = "none";   // none | text | json | binary
    public string? BodyText { get; init; }            // set for text/json
    public int ResponseStatus { get; set; }
    public string ResponseSource { get; set; } = "default"; // default | rule | override | url | error
}

/// <summary>Wire shape for list/stream payloads — small enough to poll cheaply.</summary>
public sealed record EntrySummary(
    string Id, long Seq, DateTimeOffset Time, string Method, string Path,
    int Status, string Source, long BodyBytes, string BodyKind);

public static class EntryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static EntrySummary Summarize(this CaptureEntry e) =>
        new(e.Id, e.Seq, e.Time, e.Method, e.Path, e.ResponseStatus, e.ResponseSource, e.BodyLength, e.BodyKind);

    /// <summary>Full detail for the inspector pane.</summary>
    public static object Detail(this CaptureEntry e) => new
    {
        id = e.Id,
        bucket = e.BucketId,
        seq = e.Seq,
        time = e.Time,
        method = e.Method,
        path = e.Path,
        rawPath = e.RawPath,
        clientIp = e.ClientIp,
        query = e.Query,
        headers = e.Headers,
        body = new
        {
            kind = e.BodyKind,
            bytes = e.BodyLength,
            truncated = e.BodyTruncated,
            text = e.BodyText,
        },
        response = new { status = e.ResponseStatus, source = e.ResponseSource },
    };
}

/// <summary>Fixed-capacity FIFO with eviction callbacks — the retention ring.</summary>
public sealed class RingBuffer<T>
{
    private readonly object _gate = new();
    private readonly Queue<T> _items = new();
    private int _capacity;

    public RingBuffer(int capacity) => _capacity = Math.Max(1, capacity);

    public event Action<T>? Evicted;

    public void AddCapacity(int n) { lock (_gate) _capacity = Math.Max(1, _capacity + n); }

    public T[] Snapshot() { lock (_gate) return _items.ToArray(); }

    public int Count { get { lock (_gate) return _items.Count; } }

    public void Add(T item)
    {
        T? evicted = default;
        var raised = false;
        lock (_gate)
        {
            _items.Enqueue(item);
            if (_items.Count > _capacity) { evicted = _items.Dequeue(); raised = true; }
        }
        if (raised) Evicted?.Invoke(evicted!);
    }

    public void Clear()
    {
        T[] gone;
        lock (_gate) { gone = _items.ToArray(); _items.Clear(); }
        foreach (var g in gone) Evicted?.Invoke(g);
    }
}
