namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>Base62 with the alphabet <c>0-9A-Za-z</c> (no '+' or '/'), left-padded with '0' to a fixed width.</summary>
public static class Base62
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const ulong Radix = 62;

    /// <summary>Generated codes are exactly this many characters.</summary>
    public const int CodeLength = 7;

    /// <summary>The number of distinct generated codes: 62^7 = 3,521,614,606,208. Valid ids are in <c>[0, CodeSpace)</c>.</summary>
    public const ulong CodeSpace = 3_521_614_606_208UL;

    public static string Encode(ulong value, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        return string.Create(width, value, static (buffer, remaining) =>
        {
            for (var i = buffer.Length - 1; i >= 0; i--)
            {
                buffer[i] = Alphabet[(int)(remaining % Radix)];
                remaining /= Radix;
            }

            if (remaining != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The value does not fit in the requested width.");
            }
        });
    }

    public static ulong Decode(ReadOnlySpan<char> text)
    {
        ulong result = 0;
        foreach (var c in text)
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0)
            {
                throw new FormatException($"'{c}' is not a base62 character.");
            }

            result = checked((result * Radix) + (ulong)digit);
        }

        return result;
    }
}
