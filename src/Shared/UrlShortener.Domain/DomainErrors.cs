using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain;

/// <summary>The shared vocabulary of expected domain failures.</summary>
public static class DomainErrors
{
    public static readonly Error InvalidShortCode = new(
        "ShortCode.Invalid",
        $"A short code is {DomainRules.GeneratedCodeLength} base62 characters, or {DomainRules.AliasMinLength}-{DomainRules.AliasMaxLength} characters from [A-Za-z0-9_-].",
        ErrorType.Validation);

    public static readonly Error InvalidAlias = new(
        "Alias.Invalid",
        $"An alias is {DomainRules.AliasMinLength}-{DomainRules.AliasMaxLength} characters from [A-Za-z0-9_-].",
        ErrorType.Validation);

    public static readonly Error ReservedAlias = new(
        "Alias.Reserved",
        "This alias is reserved and cannot be used.",
        ErrorType.Validation);

    public static readonly Error InvalidOriginalUrl = new(
        "OriginalUrl.Invalid",
        $"The URL must be an absolute http or https URL of at most {DomainRules.MaxUrlLength} characters, without whitespace or credentials.",
        ErrorType.Validation);

    public static readonly Error ExpirationInPast = new(
        "ShortUrl.ExpirationInPast",
        "The expiration must be in the future.",
        ErrorType.Validation);
}
