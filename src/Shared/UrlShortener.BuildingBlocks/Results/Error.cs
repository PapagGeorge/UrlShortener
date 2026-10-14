namespace UrlShortener.BuildingBlocks.Results;

/// <summary>A typed, expected failure. <paramref name="Code"/> is stable and machine-readable (e.g. <c>ShortCode.Invalid</c>).</summary>
public sealed record Error(string Code, string Description, ErrorType Type);
