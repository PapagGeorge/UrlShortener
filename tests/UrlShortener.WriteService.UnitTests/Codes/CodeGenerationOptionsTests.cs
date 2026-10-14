using Microsoft.Extensions.Options;
using UrlShortener.WriteService.Infrastructure.Codes;

namespace UrlShortener.WriteService.UnitTests.Codes;

public sealed class CodeGenerationOptionsTests
{
    private static string KeyOfLength(int bytes) => Convert.ToBase64String(Enumerable.Range(0, bytes).Select(i => (byte)(i + 1)).ToArray());

    private readonly ObfuscationOptionsValidator _obfuscation = new();
    private readonly CounterOptionsValidator _counter = new();

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    public void ObfuscationKey_Base64OfAtLeast32Bytes_IsValid(int bytes)
    {
        var options = new ObfuscationOptions { Key = KeyOfLength(bytes) };

        _obfuscation.Validate(null, options).Succeeded.ShouldBeTrue();
        options.GetKeyBytes().Length.ShouldBe(bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    public void ObfuscationKey_MissingOrNotBase64_IsInvalid(string? key)
    {
        var result = _obfuscation.Validate(null, new ObfuscationOptions { Key = key });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Obfuscation:Key");
    }

    [Fact]
    public void ObfuscationKey_ShorterThan32Bytes_IsInvalid()
    {
        var result = _obfuscation.Validate(null, new ObfuscationOptions { Key = KeyOfLength(31) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("32");
    }

    [Fact]
    public void ObfuscationKey_NeverAppearsInTheValidationMessage()
    {
        var key = KeyOfLength(10);

        var result = _obfuscation.Validate(null, new ObfuscationOptions { Key = key });

        result.FailureMessage!.ShouldNotContain(key);
    }

    [Fact]
    public void ObfuscationKey_IsNotPrintedByToString()
    {
        var key = KeyOfLength(32);

        new ObfuscationOptions { Key = key }.ToString()!.ShouldNotContain(key);
    }

    [Fact]
    public void CounterOptions_DefaultsAreValid()
    {
        var options = new CounterOptions();

        options.BatchSize.ShouldBe(1000);
        options.RedisKey.ShouldBe("urlshortener:counter");
        _counter.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void CounterBatchSize_OutOfRange_IsInvalid(int batchSize)
    {
        _counter.Validate(null, new CounterOptions { BatchSize = batchSize }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void CounterRedisKey_Blank_IsInvalid()
    {
        _counter.Validate(null, new CounterOptions { RedisKey = " " }).Failed.ShouldBeTrue();
    }
}
