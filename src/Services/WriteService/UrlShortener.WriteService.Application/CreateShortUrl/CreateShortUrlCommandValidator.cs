using FluentValidation;
using FluentValidation.Results;
using UrlShortener.Domain;

namespace UrlShortener.WriteService.Application.CreateShortUrl;

/// <summary>
/// Reuses the Domain value objects as the single source of truth, so the validator and the Domain never disagree.
/// Each failure carries the Domain error code as its <c>ErrorCode</c>.
/// </summary>
public sealed class CreateShortUrlCommandValidator : AbstractValidator<CreateShortUrlCommand>
{
    public CreateShortUrlCommandValidator(TimeProvider time, PublicHost publicHost)
    {
        RuleFor(x => x.OriginalUrl).Custom((value, context) =>
        {
            var result = OriginalUrl.Create(value);
            if (result.IsFailure)
            {
                context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description) { ErrorCode = result.Error.Code });
            }
            else if (PointsAtHost(result.Value, publicHost))
            {
                // A short link to ourselves is at best useless and at worst a redirect loop.
                context.AddFailure(new ValidationFailure(context.PropertyPath, "The URL must not point to this service.")
                {
                    ErrorCode = "OriginalUrl.SelfReference",
                });
            }
        });

        When(x => x.Alias is not null, () =>
            RuleFor(x => x.Alias).Custom((value, context) =>
            {
                var result = Alias.Create(value);
                if (result.IsFailure)
                {
                    context.AddFailure(new ValidationFailure(context.PropertyPath, result.Error.Description) { ErrorCode = result.Error.Code });
                }
            }));

        When(x => x.ExpiresAt is not null, () =>
            RuleFor(x => x.ExpiresAt).Custom((value, context) =>
            {
                if (value <= time.GetUtcNow())
                {
                    context.AddFailure(new ValidationFailure(context.PropertyPath, DomainErrors.ExpirationInPast.Description)
                    {
                        ErrorCode = DomainErrors.ExpirationInPast.Code,
                    });
                }
            }));
    }

    private static bool PointsAtHost(OriginalUrl url, PublicHost publicHost) =>
        Uri.TryCreate(url.Value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Host, publicHost.Value, StringComparison.OrdinalIgnoreCase);
}
