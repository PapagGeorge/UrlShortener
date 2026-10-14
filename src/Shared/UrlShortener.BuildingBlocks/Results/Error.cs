namespace UrlShortener.BuildingBlocks.Results;

/// <summary>
/// A typed, expected failure. <paramref name="Code"/> is stable and machine-readable (e.g. <c>ShortCode.Invalid</c>).
/// <paramref name="Details"/> optionally carries per-field messages (used for validation errors).
/// </summary>
public sealed record Error(
    string Code,
    string Description,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? Details = null);
