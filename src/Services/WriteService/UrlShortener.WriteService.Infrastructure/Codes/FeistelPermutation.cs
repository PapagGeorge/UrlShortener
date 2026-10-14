using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.WriteService.Infrastructure.Codes;

/// <summary>
/// A keyed bijection on <c>[0, domainSize)</c>: a balanced Feistel network over the smallest even number of bits
/// that covers the domain, plus cycle-walking (re-apply until the result lands inside the domain).
/// The round function is HMAC-SHA256 with one HKDF-derived key per round.
/// </summary>
public sealed class FeistelPermutation
{
    private const int Rounds = 4;
    private const int MinimumKeyLength = 32;

    private readonly ulong _domainSize;
    private readonly int _halfBits;
    private readonly ulong _halfMask;
    private readonly byte[][] _roundKeys;

    public FeistelPermutation(ReadOnlySpan<byte> key, ulong domainSize)
    {
        if (key.Length < MinimumKeyLength)
        {
            throw new ArgumentException($"The key must be at least {MinimumKeyLength} bytes.", nameof(key));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(domainSize, 2UL);

        var bits = BitOperations.Log2(domainSize - 1) + 1;
        if (bits % 2 != 0)
        {
            bits++;
        }

        _halfBits = bits / 2;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_halfBits, 32, nameof(domainSize));

        _domainSize = domainSize;
        _halfMask = (1UL << _halfBits) - 1;
        _roundKeys = new byte[Rounds][];
        for (var round = 0; round < Rounds; round++)
        {
            _roundKeys[round] = new byte[32];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                key,
                _roundKeys[round],
                salt: ReadOnlySpan<byte>.Empty,
                info: Encoding.ASCII.GetBytes($"urlshortener.feistel.v1.round.{round}"));
        }
    }

    public ulong Permute(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, _domainSize);

        do
        {
            value = Encrypt(value);
        }
        while (value >= _domainSize);

        return value;
    }

    private ulong Encrypt(ulong value)
    {
        var left = value >> _halfBits;
        var right = value & _halfMask;

        for (var round = 0; round < Rounds; round++)
        {
            (left, right) = (right, left ^ RoundFunction(_roundKeys[round], right));
        }

        return (left << _halfBits) | right;
    }

    private ulong RoundFunction(byte[] roundKey, ulong half)
    {
        Span<byte> input = stackalloc byte[sizeof(ulong)];
        Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        BinaryPrimitives.WriteUInt64BigEndian(input, half);
        HMACSHA256.HashData(roundKey, input, mac);
        return BinaryPrimitives.ReadUInt64BigEndian(mac) & _halfMask;
    }
}
