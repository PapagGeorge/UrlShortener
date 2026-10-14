using UrlShortener.Domain;
using UrlShortener.WriteService.Application.Ports;

namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>counter id → Feistel bijection on [0, 62^7) → base62, left-padded to exactly 7 characters.</summary>
public sealed class FeistelCodeObfuscator(byte[] key) : ICodeObfuscator
{
    private readonly FeistelPermutation _permutation = new(key, Base62.CodeSpace);

    public ShortCode ToCode(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((ulong)id, Base62.CodeSpace);

        var encoded = Base62.Encode(_permutation.Permute((ulong)id), Base62.CodeLength);
        return ShortCode.Create(encoded).Value;
    }
}
