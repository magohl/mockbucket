using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace MockBucket.Core;

/// <summary>
/// Stored per-bucket mock rule. Evaluated in list order, first enabled match wins.
/// Match fields are AND-ed; omitted fields match anything. Query/header values: exact string,
/// "*" = must exist, "~regex" = pattern. Path: glob with * and ?, or "~regex".
/// </summary>
public sealed class Rule
{
    public string Id { get; set; } = NewId();
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public RuleMatch Match { get; set; } = new();
    public RuleResponse Respond { get; set; } = new();

    private static string NewId()
    {
        Span<byte> b = stackalloc byte[6];
        System.Security.Cryptography.RandomNumberGenerator.Fill(b);
        return "r_" + Convert.ToBase64String(b)[..8].Replace('+', '-').Replace('/', '_');
    }
}

public sealed class RuleMatch
{
    public string? Method { get; set; }                     // "GET" etc., null = any
    public string? Path { get; set; }                       // "/api/*" glob or "~^/a.*" regex
    public Dictionary<string, string?>? Query { get; set; } // subset match
    public Dictionary<string, string?>? Headers { get; set; }
}

public sealed class RuleResponse
{
    public int Status { get; set; } = 200;
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public string? ContentType { get; set; }
}

public sealed class RuleEngine
{
    private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

    public static Rule? FirstMatch(IReadOnlyList<Rule> rules, string method, string path,
        Dictionary<string, string?[]> query, Dictionary<string, string?[]> headers)
    {
        foreach (var r in rules)
        {
            if (!r.Enabled) continue;
            if (r.Match.Method is not null && !string.Equals(r.Match.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
            if (r.Match.Path is not null && !PathMatches(r.Match.Path, path)) continue;
            if (r.Match.Query is not null && !Subset(r.Match.Query, Flatten(query))) continue;
            if (r.Match.Headers is not null && !Subset(r.Match.Headers, Flatten(headers))) continue;
            return r;
        }
        return null;
    }

    private static Dictionary<string, string?> Flatten(Dictionary<string, string?[]> q) =>
        q.ToDictionary(kv => kv.Key, kv => kv.Value.Length > 0 ? kv.Value[0] : null, StringComparer.OrdinalIgnoreCase);

    private static bool Subset(Dictionary<string, string?> want, Dictionary<string, string?> have)
    {
        var haveCi = new Dictionary<string, string?>(have, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in want)
        {
            if (!haveCi.TryGetValue(k, out var actual)) return false;
            if (v is "*" or null) continue;                    // existence only
            if (!ValueMatches(v, actual)) return false;
        }
        return true;
    }

    private static bool ValueMatches(string want, string? actual) =>
        want.StartsWith('~') ? Rx(want, actual) : want == actual;

    private static bool PathMatches(string pattern, string path) =>
        pattern.StartsWith('~') ? Rx(pattern, path) : Glob(pattern, path);

    private static bool Rx(string pattern, string? input) =>
        input is not null && Compiled(pattern[1..]).IsMatch(input);

    private static bool Glob(string pattern, string path) =>
        Compiled("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$").IsMatch(path);

    private static Regex Compiled(string p) =>
        GlobCache.GetOrAdd(p, s => new Regex(s, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
}
