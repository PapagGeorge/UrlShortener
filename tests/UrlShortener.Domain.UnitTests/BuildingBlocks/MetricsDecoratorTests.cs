using System.Diagnostics.Metrics;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Metrics;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain.UnitTests.BuildingBlocks;

public sealed class MetricsDecoratorTests
{
    private sealed record MetricsTestCommand;

    private sealed record MetricsTestQuery;

    private sealed class StubCommandHandler(Func<Result<int>> behaviour) : ICommandHandler<MetricsTestCommand, int>
    {
        public Task<Result<int>> Handle(MetricsTestCommand command, CancellationToken ct) => Task.FromResult(behaviour());
    }

    private sealed class StubQueryHandler(Func<Result<int>> behaviour) : IQueryHandler<MetricsTestQuery, int>
    {
        public Task<Result<int>> Handle(MetricsTestQuery query, CancellationToken ct) => Task.FromResult(behaviour());
    }

    /// <summary>Collects the measurements the decorators emit for one request type.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(string Instrument, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

        public Recorder(string requestName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == HandlerMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(requestName, i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(requestName, i, v, tags));
            _listener.Start();
        }

        private void Add(string requestName, Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
            if (dictionary.TryGetValue("request", out var request) && (string?)request == requestName)
            {
                lock (Measurements)
                {
                    Measurements.Add((instrument.Name, value, dictionary));
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Command_Success_CountsOneRequestWithOutcomeSuccessAndRecordsDuration()
    {
        using var recorder = new Recorder(nameof(MetricsTestCommand));
        var sut = new MetricsCommandDecorator<MetricsTestCommand, int>(new StubCommandHandler(() => 1));

        var result = await sut.Handle(new MetricsTestCommand(), CancellationToken.None);

        result.Value.ShouldBe(1);
        var counted = recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.RequestsName).ShouldHaveSingleItem();
        counted.Value.ShouldBe(1);
        counted.Tags["outcome"].ShouldBe("success");
        recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.DurationName).ShouldHaveSingleItem()
            .Tags["outcome"].ShouldBe("success");
    }

    [Fact]
    public async Task Command_ExpectedFailure_UsesTheErrorTypeAsOutcomeAndReturnsTheSameResult()
    {
        using var recorder = new Recorder(nameof(MetricsTestCommand));
        var error = new Error("X.Conflict", "taken", ErrorType.Conflict);
        var sut = new MetricsCommandDecorator<MetricsTestCommand, int>(new StubCommandHandler(() => error));

        var result = await sut.Handle(new MetricsTestCommand(), CancellationToken.None);

        result.Error.ShouldBe(error);
        recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.RequestsName).ShouldHaveSingleItem()
            .Tags["outcome"].ShouldBe("conflict");
    }

    [Fact]
    public async Task Command_Exception_CountsOutcomeExceptionAndRethrows()
    {
        using var recorder = new Recorder(nameof(MetricsTestCommand));
        var sut = new MetricsCommandDecorator<MetricsTestCommand, int>(
            new StubCommandHandler(() => throw new InvalidOperationException("boom")));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.Handle(new MetricsTestCommand(), CancellationToken.None));

        recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.RequestsName).ShouldHaveSingleItem()
            .Tags["outcome"].ShouldBe("exception");
        recorder.Measurements.Count(m => m.Instrument == HandlerMetrics.DurationName).ShouldBe(1);
    }

    [Fact]
    public async Task Query_Success_CountsOneRequest()
    {
        using var recorder = new Recorder(nameof(MetricsTestQuery));
        var sut = new MetricsQueryDecorator<MetricsTestQuery, int>(new StubQueryHandler(() => 2));

        var result = await sut.Handle(new MetricsTestQuery(), CancellationToken.None);

        result.Value.ShouldBe(2);
        recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.RequestsName).ShouldHaveSingleItem()
            .Tags["outcome"].ShouldBe("success");
    }

    [Fact]
    public async Task Query_Gone_UsesGoneAsOutcome()
    {
        using var recorder = new Recorder(nameof(MetricsTestQuery));
        var sut = new MetricsQueryDecorator<MetricsTestQuery, int>(
            new StubQueryHandler(() => new Error("X.Expired", "expired", ErrorType.Gone)));

        await sut.Handle(new MetricsTestQuery(), CancellationToken.None);

        recorder.Measurements.Where(m => m.Instrument == HandlerMetrics.RequestsName).ShouldHaveSingleItem()
            .Tags["outcome"].ShouldBe("gone");
    }
}
