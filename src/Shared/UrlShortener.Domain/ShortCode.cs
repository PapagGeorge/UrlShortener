using System.Buffers;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain;

/// <summary>
/// The path segment that identifies a short URL. Either a generated code (exactly 7 base62 characters)
/// or an alias (8-30 characters from <c>[A-Za-z0-9_-]</c>). Case-sensitive.
/// </summary>
public readonly record struct ShortCode
{
    private static readonly SearchValues<char> Base62 =
        SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    private static readonly SearchValues<char> AliasCharacters =
        SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_");

    private ShortCode(string value) => Value = value;

    public string Value { get; }

    public bool IsReserved => DomainRules.IsReserved(Value);

    /// <summary>Allocation-free gate for the read path: a malformed code never reaches Redis or the database.</summary>
    public static bool TryParse(string? input, out ShortCode code)
    {
        if (IsValid(input))
        {
            code = new ShortCode(input!);
            return true;
        }

        code = default;
        return false;
    }

    public static Result<ShortCode> Create(string? input) =>
        TryParse(input, out var code) ? code : DomainErrors.InvalidShortCode;

    public override string ToString() => Value;

    private static bool IsValid(string? input)
    {
        if (input is null)
        {
            return false;
        }

        var span = input.AsSpan();
        return span.Length switch
        {
            DomainRules.GeneratedCodeLength => !span.ContainsAnyExcept(Base62),
            >= DomainRules.AliasMinLength and <= DomainRules.AliasMaxLength => !span.ContainsAnyExcept(AliasCharacters),
            _ => false,
        };
    }
}
