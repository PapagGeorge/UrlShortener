using System.Security.Cryptography;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class FeistelPermutationTests
{
    private static byte[] Key(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray();

    [Theory]
    [InlineData(2UL)]
    [InlineData(1000UL)]       // not a power of two: needs cycle-walking
    [InlineData(1024UL)]       // exact power of two: no cycle-walking
    [InlineData(100_003UL)]    // odd prime
    public void Permute_SmallDomain_IsAnExhaustiveBijection(ulong domainSize)
    {
        var permutation = new FeistelPermutation(Key(1), domainSize);

        var outputs = new HashSet<ulong>();
        for (ulong id = 0; id < domainSize; id++)
        {
            var output = permutation.Permute(id);
            output.ShouldBeLessThan(domainSize);
            outputs.Add(output).ShouldBeTrue($"{id} collided with an earlier id");
        }

        outputs.Count.ShouldBe((int)domainSize);
    }

    [Fact]
    public void Permute_RealCodeSpace_IsInjectiveOverLargeRangeAndBoundaries()
    {
        var permutation = new FeistelPermutation(Key(9), Base62.CodeSpace);
        var random = new Random(123);
        var ids = Enumerable.Range(0, 400_000).Select(i => (ulong)i)
            .Concat(Enumerable.Range(0, 100_000).Select(_ => (ulong)random.NextInt64(0, (long)Base62.CodeSpace)))
            .Concat([Base62.CodeSpace - 1, Base62.CodeSpace - 2, Base62.CodeSpace / 2])
            .Distinct()
            .ToArray();

        var outputs = new HashSet<ulong>();
        foreach (var id in ids)
        {
            var output = permutation.Permute(id);
            output.ShouldBeLessThan(Base62.CodeSpace);
            outputs.Add(output).ShouldBeTrue($"{id} collided with an earlier id");
        }

        outputs.Count.ShouldBe(ids.Length);
    }

    [Fact]
    public void Permute_SequentialIds_DoNotProduceSequentialOutputs()
    {
        var permutation = new FeistelPermutation(Key(3), Base62.CodeSpace);

        var outputs = Enumerable.Range(0, 100).Select(i => permutation.Permute((ulong)i)).ToArray();

        outputs.ShouldNotBe(outputs.Order().ToArray());
        outputs.Zip(outputs.Skip(1), (a, b) => b == a + 1).ShouldAllBe(isNext => !isNext);
    }

    [Fact]
    public void Permute_SameKey_IsDeterministic()
    {
        var a = new FeistelPermutation(Key(5), Base62.CodeSpace);
        var b = new FeistelPermutation(Key(5), Base62.CodeSpace);

        Enumerable.Range(0, 1000).ShouldAllBe(i => a.Permute((ulong)i) == b.Permute((ulong)i));
    }

    [Fact]
    public void Permute_DifferentKeys_ProduceDifferentOutputs()
    {
        var a = new FeistelPermutation(Key(5), Base62.CodeSpace);
        var b = new FeistelPermutation(Key(6), Base62.CodeSpace);

        var differing = Enumerable.Range(0, 1000).Count(i => a.Permute((ulong)i) != b.Permute((ulong)i));

        differing.ShouldBeGreaterThan(990);
    }

    [Theory]
    [InlineData(1000UL)]
    [InlineData(1001UL)]
    public void Permute_ValueOutsideDomain_Throws(ulong value)
    {
        var permutation = new FeistelPermutation(Key(1), 1000);

        Should.Throw<ArgumentOutOfRangeException>(() => permutation.Permute(value));
    }

    [Fact]
    public void Constructor_KeyShorterThan32Bytes_Throws()
    {
        Should.Throw<ArgumentException>(() => new FeistelPermutation(RandomNumberGenerator.GetBytes(31), 1000));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    public void Constructor_DomainSmallerThanTwo_Throws(ulong domainSize)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FeistelPermutation(Key(1), domainSize));
    }
}
