using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class Base62Tests
{
    [Theory]
    [InlineData(0UL, "0000000")]
    [InlineData(1UL, "0000001")]
    [InlineData(9UL, "0000009")]
    [InlineData(10UL, "000000A")]      // alphabet order: 0-9, A-Z, a-z
    [InlineData(35UL, "000000Z")]
    [InlineData(36UL, "000000a")]
    [InlineData(61UL, "000000z")]
    [InlineData(62UL, "0000010")]
    [InlineData(1_000_000_000UL, "015ftgG")]   // '15ftgG' in docs/system-design.md, padded to 7
    [InlineData(3_521_614_606_207UL, "zzzzzzz")] // 62^7 - 1
    public void Encode_KnownValues_ReturnsExpectedPaddedString(ulong value, string expected)
    {
        Base62.Encode(value, Base62.CodeLength).ShouldBe(expected);
    }

    [Fact]
    public void CodeSpace_Is62ToThePowerOf7()
    {
        Base62.CodeSpace.ShouldBe(3_521_614_606_208UL);
    }

    [Fact]
    public void Encode_ValueBeyondWidth_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Base62.Encode(Base62.CodeSpace, Base62.CodeLength));
    }

    [Fact]
    public void Decode_InvertsEncode_ForSmallAndRandomValues()
    {
        var random = new Random(42);
        var values = Enumerable.Range(0, 100_000).Select(i => (ulong)i)
            .Concat(Enumerable.Range(0, 100_000).Select(_ => (ulong)random.NextInt64(0, (long)Base62.CodeSpace)))
            .Append(Base62.CodeSpace - 1);

        foreach (var value in values)
        {
            Base62.Decode(Base62.Encode(value, Base62.CodeLength)).ShouldBe(value);
        }
    }

    [Fact]
    public void Encode_AlwaysProducesExactlySevenCharacters()
    {
        var random = new Random(7);
        for (var i = 0; i < 10_000; i++)
        {
            var value = (ulong)random.NextInt64(0, (long)Base62.CodeSpace);
            Base62.Encode(value, Base62.CodeLength).Length.ShouldBe(7);
        }
    }

    [Theory]
    [InlineData("abc-def")]
    [InlineData("abc def")]
    [InlineData("αβγ")]
    public void Decode_InvalidCharacter_Throws(string input)
    {
        Should.Throw<FormatException>(() => Base62.Decode(input));
    }
}
