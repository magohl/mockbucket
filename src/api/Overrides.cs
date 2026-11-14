using Microsoft.Extensions.Primitives;

namespace MockBucket.Core;

/// <summary>
/// Request-side response overrides — mb_* query params decide the response of a single call.
/// mb_status=401 · mb_header=X-Key: v (repeatable) · mb_body=… · mb_type=application/json
/// Malformed controls get clean 400s and are never silently ignored.
/// </summary>
public sealed record OverrideResponse(int Status, Dictionary<string, string> Headers, string? Body, string? ContentType);

public sealed class OverrideException(List<string> errors) : Exception(string.Join("; ", errors))
{
    public List<string> Errors { get; } = errors;
}

public static class Overrides
{
    public static bool Present(IQueryCollection query) =>
        query.Any(kv => kv.Key.StartsWith("mb_", StringComparison.OrdinalIgnoreCase));

    public static OverrideResponse Parse(IQueryCollection query)
    {
        var errors = new List<string>();
        var status = 200;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? body = null, type = null;
        bool any = false;

        foreach (var (key, values) in query)
        {
            if (!key.StartsWith("mb_", StringComparison.OrdinalIgnoreCase)) continue;
            any = true;
            switch (key[3..].ToLowerInvariant())
            {
                case "status":
                    if (values.Count != 1 || !int.TryParse(values[0], out status) || status is < 100 or > 599)
                    { errors.Add("mb_status must be a single integer in 100..599"); status = 200; }
                    break;
                case "header":
                    foreach (var v in values)
                    {
                        if (v is null) { errors.Add("mb_header value missing"); continue; }
                        var i = v.IndexOf(':');
                        if (i <= 0 || i == v.Length - 1 || !IsValidName(v[..i]))
                            errors.Add($"mb_header needs 'Name: value' form, got '{v}'");
                        else headers[v[..i].Trim()] = v[(i + 1)..].Trim();
                    }
                    break;
                case "body":
                    if (values.Count != 1 || values[0] is null) errors.Add("mb_body must appear exactly once");
                    else { body = values[0]; any = true; }
                    break;
                case "type":
                    if (values.Count != 1 || values[0] is not { Length: > 0 } || !IsValidValue(values[0]!)) errors.Add("mb_type must be a single valid content type");
                    else type = values[0];
                    break;
                default:
                    errors.Add($"unknown override parameter '{key}' (mb_status|mb_header|mb_body|mb_type)");
                    break;
            }
        }

        if (errors.Count > 0) throw new OverrideException(errors);
        return any ? new OverrideResponse(status, headers, body, type) : null!;
    }

    private static bool IsValidName(string n) =>
        n.Length > 0 && n.Trim() == n && n.All(c => !char.IsControl(c) && !"()<>@,;:\\\"/[]?={} \t".Contains(c));

    private static bool IsValidValue(string v) => v.Length > 0 && v.All(c => !char.IsControl(c));
}
