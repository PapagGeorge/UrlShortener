using UrlShortener.Domain;
using UrlShortener.WriteService.Application.Ports;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class FeistelCodeObfuscatorTests
{
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 1)).ToArray();

    private static ICodeObfuscator Create(byte[]? key = null) => new FeistelCodeObfuscator(key ?? TestKey);

    [Fact]
    public void ToCode_AlwaysReturnsAValidSevenCharacterGeneratedCode()
    {
        var sut = Create();
        var random = new Random(11);
        var ids = Enumerable.Range(0, 50_000).Select(i => (long)i)
            .Concat(Enumerable.Range(0, 50_000).Select(_ => random.NextInt64(0, (long)Base62.CodeSpace)))
            .Append((long)Base62.CodeSpace - 1);

        foreach (var id in ids)
        {
            var code = sut.ToCode(id);

            code.Value.Length.ShouldBe(DomainRules.GeneratedCodeLength);
            ShortCode.TryParse(code.Value, out _).ShouldBeTrue();
        }
    }

    [Fact]
    public void ToCode_DistinctIds_ProduceDistinctCodes()
    {
        var sut = Create();

        var codes = Enumerable.Range(0, 300_000).Select(i => sut.ToCode(i).Value).ToHashSet();

        codes.Count.ShouldBe(300_000);
    }

    [Fact]
    public void ToCode_SameId_IsDeterministic()
    {
        Create().ToCode(123_456).ShouldBe(Create().ToCode(123_456));
    }

    [Fact]
    public void ToCode_DifferentKeys_ProduceDifferentCodes()
    {
        var otherKey = TestKey.Select(b => (byte)(b ^ 0x5A)).ToArray();

        Create().ToCode(42).ShouldNotBe(Create(otherKey).ToCode(42));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(3_521_614_606_208L)]   // 62^7: first id outside the code space
    [InlineData(long.MaxValue)]
    public void ToCode_IdOutsideCodeSpace_Throws(long id)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Create().ToCode(id));
    }

    /// <summary>
    /// Characterization test: pins the algorithm. Changing the Feistel design, rounds, key derivation or alphabet
    /// re-maps every code, which would collide with rows already stored in production. If this fails, do not "fix" the vectors.
    /// </summary>
    [Theory]
    [InlineData(0L, "71R254w")]
    [InlineData(1L, "VoueQKN")]
    [InlineData(2L, "m7RlwaH")]
    [InlineData(1_000_000_000L, "mkMEH7G")]
    [InlineData(3_521_614_606_207L, "XxPbPvA")]
    public void ToCode_KnownVectors_StayStable(long id, string expected)
    {
        Create().ToCode(id).Value.ShouldBe(expected);
    }
}
