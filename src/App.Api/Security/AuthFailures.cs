using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace App.Api.Security;

public static class AuthReasons
{
    public const string Missing = "token_missing";

    public const string Expired = "token_expired";

    public const string ForeignIssuer = "token_foreign_issuer";

    public const string WrongAudience = "token_wrong_audience";

    public const string BadSignature = "token_bad_signature";

    public const string OtherTokenError = "token_invalid_other";

    public const string RoleMissing = "authz_role_missing";

    public const string NotOwner = "authz_not_owner";

    public static readonly string[] All =
    [
        Missing, Expired, ForeignIssuer, WrongAudience,
        BadSignature, OtherTokenError, RoleMissing, NotOwner
    ];
}

public sealed class AuthFailureCounters
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public void Count(string reason) =>
        _counts.AddOrUpdate(reason, 1, static (_, current) => current + 1);

    public IReadOnlyDictionary<string, int> Snapshot()
    {
        var result = new Dictionary<string, int>(AuthReasons.All.Length);
        foreach (var reason in AuthReasons.All)
            result[reason] = _counts.TryGetValue(reason, out var n) ? n : 0;
        return result;
    }

    public void Reset() => _counts.Clear();
}

public static class TokenPeek
{
    public static string? UnverifiedIssuer(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken)) return null;

        var parts = bearerToken.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            using var document = JsonDocument.Parse(FromBase64Url(parts[1]));
            return document.RootElement.TryGetProperty("iss", out var iss)
                ? iss.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? FromAuthorizationHeader(string? headerValue)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(headerValue)) return null;
        return headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? headerValue[prefix.Length..].Trim()
            : null;
    }

    private static byte[] FromBase64Url(string value)
    {
        var normalized = new StringBuilder(value.Replace('-', '+').Replace('_', '/'));
        while (normalized.Length % 4 != 0) normalized.Append('=');
        return Convert.FromBase64String(normalized.ToString());
    }
}
