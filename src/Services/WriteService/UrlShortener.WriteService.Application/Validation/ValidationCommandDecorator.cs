using FluentValidation;
using UrlShortener.BuildingBlocks.Messaging;
using UrlShortener.BuildingBlocks.Results;

namespace UrlShortener.WriteService.Application.Validation;

/// <summary>Runs every registered FluentValidation validator before the handler; failures become a <c>Validation.Failed</c> error (HTTP 400).</summary>
public sealed class ValidationCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
{
    public static readonly Error ValidationFailed = new("Validation.Failed", "One or more validation errors occurred.", ErrorType.Validation);

    public async Task<Result<TResult>> Handle(TCommand command, CancellationToken ct)
    {
        var results = await Task.WhenAll(validators.Select(validator => validator.ValidateAsync(command, ct)));

        var failures = results.SelectMany(r => r.Errors).ToArray();
        if (failures.Length == 0)
        {
            return await inner.Handle(command, ct);
        }

        var details = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        return ValidationFailed with { Details = details };
    }
}
