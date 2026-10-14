using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain;

/// <summary>A user-chosen short code. Length 8-30 guarantees it can never equal a generated (7-character) code.</summary>
public readonly record struct Alias
{
    private Alias(ShortCode code) => Code = code;

    public ShortCode Code { get; }

    public static Result<Alias> Create(string? input)
    {
        if (input is not null && DomainRules.IsReserved(input))
        {
            return DomainErrors.ReservedAlias;
        }

        if (input is null
            || input.Length < DomainRules.AliasMinLength
            || !ShortCode.TryParse(input, out var code))
        {
            return DomainErrors.InvalidAlias;
        }

        return new Alias(code);
    }

    public override string ToString() => Code.Value;
}
