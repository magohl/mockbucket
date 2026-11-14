using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MockBucket.Core;

/// <summary>A bucket: metadata + entry ring + rules + last activity for idle expiry.</summary>
public sealed class Bucket(string id, string? name, int ringCapacity)
{
    public string Id { get; } = id;
    public string? Name { get; set; } = name;
    public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public RingBuffer<CaptureEntry> Entries { get; } = new(ringCapacity);
    public List<Rule> Rules { get; } = [];
    public long NextSeq;

    public BucketInfo Info => new(Id, Name, Created, Entries.Count, LastSeen);
}

public sealed record BucketInfo(string Id, string? Name, DateTimeOffset Created, int EntryCount, DateTimeOffset LastSeen);

public sealed record CreateBucketRequest(string? Name);

/// <summary>Env-driven knobs, read once at startup (config is 100% env).</summary>
public sealed record CaptureOptions
{
    public bool AutoCreate { get; init; }
    public int MaxEntries { get; init; } = 500;                    // MB_RETENTION_MAX_ENTRIES
    public int IdleExpiryHours { get; init; } = 24;                // MB_RETENTION_IDLE_HOURS
    public int BodyCapBytes { get; init; } = 1 << 20;              // MB_CAP_MB (whole MBs)

    public static CaptureOptions FromEnvironment() => new()
    {
        AutoCreate = Environment.GetEnvironmentVariable("MB_AUTO_CREATE") == "true",
        MaxEntries = EnvInt("MB_RETENTION_MAX_ENTRIES", 500),
        IdleExpiryHours = EnvInt("MB_RETENTION_IDLE_HOURS", 24),
        BodyCapBytes = Math.Max(1, EnvInt("MB_CAP_MB", 1)) * (1 << 20),
    };

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}

/// <summary>Registry with short Crockford base32 ids — typeable, sayable.</summary>
public sealed class BucketRegistry
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly CaptureOptions _options;

    public BucketRegistry(CaptureOptions options) => _options = options;

    public int Count => _buckets.Count;

    public Bucket? Get(string id) => _buckets.GetValueOrDefault(NormalizeId(id));

    public Bucket Create(string? name)
    {
        while (true)
        {
            var b = new Bucket(NewId(), name is { Length: <= 64 } ? name.Trim() : null, _options.MaxEntries);
            var winner = _buckets.GetOrAdd(b.Id, b);
            if (ReferenceEquals(winner, b)) return b; // 30-bit space: real collisions need a fresh draw
        }
    }

    /// <summary>Optional: capture may mint buckets on demand, but only with valid-shaped ids.</summary>
    public Bucket? GetOrCreate(string id, bool allowCreate)
    {
        id = NormalizeId(id);
        if (_buckets.TryGetValue(id, out var existing)) return existing;
        if (!allowCreate || !ValidId(id)) return null;
        var b = new Bucket(id, null, _options.MaxEntries);
        return _buckets.GetOrAdd(id, b);
    }

    public IReadOnlyList<BucketInfo> List() =>
        _buckets.Values.OrderByDescending(b => b.LastSeen).Select(b => b.Info).ToArray();

    public IReadOnlyList<Bucket> All() => _buckets.Values.ToArray();

    public bool Remove(string id) => _buckets.TryRemove(NormalizeId(id), out _);

    // Crockford Base32 (no I L O U): typeable uppercase, 6 chars = ~30 bits.
    // Readability beats entropy here: deployments assume proxy-side auth,
    // and Create() retries on collision so bucket count can't silently alias anyone's data.
    private const string IdAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Splits a capture-URL id segment into (bucketId, optional status suffix):
    /// "/b/{id}.418" lets callers who control only the base URL configure the response from the URL.
    /// A malformed suffix is returned as an error so capture can 400 cleanly; suffixes never auto-create.
    /// </summary>
    public static (string Id, int? Status, string? Error) ParseSpec(string raw)
    {
        var s = NormalizeId(raw); // dots survive; typo letters in the suffix still map I/L→1, O→0
        var dot = s.IndexOf('.');
        if (dot < 0) return (s, null, null);
        var tail = s[(dot + 1)..];
        if (tail.Contains('.') || !int.TryParse(tail, out var status) || status is < 100 or > 599)
            return (s[..dot], null, $"malformed status suffix '.{tail}' — expected /b/{{id}}.{{100..599}}");
        return (s[..dot], status, null);
    }

    public static bool ValidId(string id) =>
        id.Length == 6 && id.All(IdAlphabet.Contains);

    /// <summary>Canonical Crockford decoding: case-insensitive, hyphens (display grouping) ignored, I/L→1, O→0.</summary>
    public static string NormalizeId(string id) =>
        new(id.Trim().ToUpperInvariant()
            .Where(c => c is not ('-' or ' '))
            .Select(c => c is 'I' or 'L' ? '1' : c == 'O' ? '0' : c)
            .ToArray());

    internal static string NewId()
    {
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++) chars[i] = IdAlphabet[RandomNumberGenerator.GetInt32(IdAlphabet.Length)];
        return new string(chars);
    }
}
