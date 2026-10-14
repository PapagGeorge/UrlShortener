using FluentValidation;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.WriteService.Application.Validation;

namespace UrlShortener.WriteService.UnitTests.Application;

public sealed class ValidationCommandDecoratorTests
{
    private sealed record TestCommand(string Name, int Age);

    private sealed class NameValidator : AbstractValidator<TestCommand>
    {
        public NameValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
    }

    private sealed class AgeValidator : AbstractValidator<TestCommand>
    {
        public AgeValidator() => RuleFor(x => x.Age).GreaterThan(0).WithMessage("Age must be positive");
    }

    private sealed class SpyHandler : ICommandHandler<TestCommand, string>
    {
        public int Calls { get; private set; }

        public CancellationToken ReceivedToken { get; private set; }

        public Task<Result<string>> Handle(TestCommand command, CancellationToken ct)
        {
            Calls++;
            ReceivedToken = ct;
            return Task.FromResult<Result<string>>("handled");
        }
    }

    [Fact]
    public async Task Handle_ValidCommand_CallsTheInnerHandlerAndReturnsItsResult()
    {
        var inner = new SpyHandler();
        var sut = new ValidationCommandDecorator<TestCommand, string>(inner, [new NameValidator(), new AgeValidator()]);
        using var cts = new CancellationTokenSource();

        var result = await sut.Handle(new TestCommand("Ada", 36), cts.Token);

        result.Value.ShouldBe("handled");
        inner.Calls.ShouldBe(1);
        inner.ReceivedToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Handle_NoValidatorsRegistered_CallsTheInnerHandler()
    {
        var inner = new SpyHandler();
        var sut = new ValidationCommandDecorator<TestCommand, string>(inner, []);

        var result = await sut.Handle(new TestCommand("", 0), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_InvalidCommand_ReturnsAValidationErrorWithoutCallingTheInnerHandler()
    {
        var inner = new SpyHandler();
        var sut = new ValidationCommandDecorator<TestCommand, string>(inner, [new NameValidator()]);

        var result = await sut.Handle(new TestCommand("", 5), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Validation.Failed");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details.ShouldNotBeNull()["Name"].ShouldBe(["Name is required"]);
        inner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_FailuresFromSeveralValidators_AreCollectedPerField()
    {
        var sut = new ValidationCommandDecorator<TestCommand, string>(new SpyHandler(), [new NameValidator(), new AgeValidator()]);

        var result = await sut.Handle(new TestCommand("", -1), CancellationToken.None);

        var details = result.Error.Details.ShouldNotBeNull();
        details.Keys.ShouldBe(["Name", "Age"], ignoreOrder: true);
        details["Age"].ShouldBe(["Age must be positive"]);
    }
}
