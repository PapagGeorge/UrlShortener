using Microsoft.Extensions.Logging;
using UrlShortener.BuildingBlocks.Logging;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.Domain.UnitTests.BuildingBlocks;

public sealed class LoggingDecoratorTests
{
    private sealed record TestCommand(string Secret);

    private sealed record TestQuery(string Secret);

    private sealed class StubCommandHandler(Func<Result<int>> behaviour) : ICommandHandler<TestCommand, int>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<Result<int>> Handle(TestCommand command, CancellationToken ct)
        {
            ReceivedToken = ct;
            return Task.FromResult(behaviour());
        }
    }

    private sealed class StubQueryHandler(Func<Result<int>> behaviour) : IQueryHandler<TestQuery, int>
    {
        public Task<Result<int>> Handle(TestQuery query, CancellationToken ct) => Task.FromResult(behaviour());
    }

    private static LoggingCommandDecorator<TestCommand, int> CommandDecorator(
        ICommandHandler<TestCommand, int> inner, ListLogger<LoggingCommandDecorator<TestCommand, int>> logger) => new(inner, logger);

    private static LoggingQueryDecorator<TestQuery, int> QueryDecorator(
        IQueryHandler<TestQuery, int> inner, ListLogger<LoggingQueryDecorator<TestQuery, int>> logger) => new(inner, logger);

    [Fact]
    public async Task Command_Success_ReturnsInnerResultAndLogsInformation()
    {
        var logger = new ListLogger<LoggingCommandDecorator<TestCommand, int>>();
        var sut = CommandDecorator(new StubCommandHandler(() => 7), logger);

        var result = await sut.Handle(new TestCommand("s3cret"), CancellationToken.None);

        result.Value.ShouldBe(7);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain(nameof(TestCommand));
    }

    [Fact]
    public async Task Command_NeverLogsThePayload()
    {
        var logger = new ListLogger<LoggingCommandDecorator<TestCommand, int>>();
        var sut = CommandDecorator(new StubCommandHandler(() => 7), logger);

        await sut.Handle(new TestCommand("s3cret"), CancellationToken.None);

        logger.Entries.ShouldAllBe(e => !e.Message.Contains("s3cret"));
    }

    [Fact]
    public async Task Command_PassesCancellationTokenToInnerHandler()
    {
        using var cts = new CancellationTokenSource();
        var inner = new StubCommandHandler(() => 1);
        var sut = CommandDecorator(inner, new ListLogger<LoggingCommandDecorator<TestCommand, int>>());

        await sut.Handle(new TestCommand("x"), cts.Token);

        inner.ReceivedToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Command_ExpectedFailure_ReturnsSameFailureAndLogsInformationWithErrorCode()
    {
        var error = new Error("Alias.Taken", "taken", ErrorType.Conflict);
        var logger = new ListLogger<LoggingCommandDecorator<TestCommand, int>>();
        var sut = CommandDecorator(new StubCommandHandler(() => error), logger);

        var result = await sut.Handle(new TestCommand("x"), CancellationToken.None);

        result.Error.ShouldBe(error);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("Alias.Taken");
    }

    [Fact]
    public async Task Command_UnexpectedFailure_LogsError()
    {
        var error = new Error("Counter.Exhausted", "boom", ErrorType.Failure);
        var logger = new ListLogger<LoggingCommandDecorator<TestCommand, int>>();
        var sut = CommandDecorator(new StubCommandHandler(() => error), logger);

        await sut.Handle(new TestCommand("x"), CancellationToken.None);

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task Command_HandlerThrows_LogsErrorWithExceptionAndRethrows()
    {
        var boom = new InvalidOperationException("boom");
        var logger = new ListLogger<LoggingCommandDecorator<TestCommand, int>>();
        var sut = CommandDecorator(new StubCommandHandler(() => throw boom), logger);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => sut.Handle(new TestCommand("x"), CancellationToken.None));

        thrown.ShouldBeSameAs(boom);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(boom);
    }

    [Fact]
    public async Task Query_Success_ReturnsInnerResultAndLogsInformationWithoutPayload()
    {
        var logger = new ListLogger<LoggingQueryDecorator<TestQuery, int>>();
        var sut = QueryDecorator(new StubQueryHandler(() => 5), logger);

        var result = await sut.Handle(new TestQuery("s3cret"), CancellationToken.None);

        result.Value.ShouldBe(5);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain(nameof(TestQuery));
        entry.Message.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task Query_HandlerThrows_LogsErrorAndRethrows()
    {
        var logger = new ListLogger<LoggingQueryDecorator<TestQuery, int>>();
        var sut = QueryDecorator(new StubQueryHandler(() => throw new TimeoutException()), logger);

        await Should.ThrowAsync<TimeoutException>(() => sut.Handle(new TestQuery("x"), CancellationToken.None));

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }
}
