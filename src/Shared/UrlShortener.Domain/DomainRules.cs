using System.Collections.Frozen;

namespace UrlShortener.Domain;

/// <summary>Rule constants shared by the value objects and by the validators of both services, so they never disagree.</summary>
public static class DomainRules
{
    /// <summary>Generated codes are always exactly this long (base62, left-padded).</summary>
    public const int GeneratedCodeLength = 7;

    /// <summary>Aliases start above <see cref="GeneratedCodeLength"/>, so an alias can never collide with a generated code.</summary>
    public const int AliasMinLength = 8;

    public const int AliasMaxLength = 30;

    public const int MaxUrlLength = 2048;

    /// <summary>Words that must never become a short code: they are routes of the services (matched case-insensitively).</summary>
    public static readonly FrozenSet<string> ReservedWords = new[]
    {
        "urls", "api", "health", "metrics", "openapi", "scalar", "swagger", "admin", "static",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsReserved(string value) => ReservedWords.Contains(value);
}
