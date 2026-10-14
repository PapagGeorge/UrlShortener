using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain;

/// <summary>
/// The long URL a short code redirects to: absolute, http or https only, bounded length, no whitespace and no
/// embedded credentials. Stored exactly as given (no canonicalization, since there is no deduplication).
/// </summary>
public readonly record struct OriginalUrl
{
    private OriginalUrl(string value) => Value = value;

    public string Value { get; }

    public static Result<OriginalUrl> Create(string? input)
    {
        if (!IsValid(input))
        {
            return DomainErrors.InvalidOriginalUrl;
        }

        return new OriginalUrl(input!);
    }

    public override string ToString() => Value;

    private static bool IsValid(string? input)
    {
        if (string.IsNullOrEmpty(input) || input.Length > DomainRules.MaxUrlLength)
        {
            return false;
        }

        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return Uri.TryCreate(input, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && uri.UserInfo.Length == 0
               && uri.Host.Length > 0;
    }
}
