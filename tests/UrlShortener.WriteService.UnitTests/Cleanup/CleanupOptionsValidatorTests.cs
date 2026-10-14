using Microsoft.Extensions.Options;
using UrlShortener.WriteService.Infrastructure.Cleanup;

namespace UrlShortener.WriteService.UnitTests.Cleanup;

public sealed class CleanupOptionsValidatorTests
{
    private readonly CleanupOptionsValidator _sut = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var options = new CleanupOptions();

        options.Interval.ShouldBe(TimeSpan.FromMinutes(10));
        options.BatchSize.ShouldBe(1000);
        _sut.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3600, 10_000)]
    public void Boundaries_AreValid(int intervalSeconds, int batchSize)
    {
        var options = new CleanupOptions { Interval = TimeSpan.FromSeconds(intervalSeconds), BatchSize = batchSize };

        _sut.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(500)]   // 500 ms: below the 1 second minimum
    public void Interval_BelowOneSecond_IsInvalid(int milliseconds)
    {
        var options = new CleanupOptions { Interval = TimeSpan.FromMilliseconds(milliseconds) };

        var result = _sut.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(CleanupOptions.Interval));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void BatchSize_OutOfRange_IsInvalid(int batchSize)
    {
        var result = _sut.Validate(null, new CleanupOptions { BatchSize = batchSize });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(CleanupOptions.BatchSize));
    }
}
