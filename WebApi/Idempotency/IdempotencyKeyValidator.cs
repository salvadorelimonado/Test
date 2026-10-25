using FluentValidation;

namespace WebApi.Idempotency;

public sealed class IdempotencyKeyValidator : AbstractValidator<IdempotencyKey>
{
    public IdempotencyKeyValidator()
    {
        RuleFor(key => key.Value).Must(value => !string.IsNullOrWhiteSpace(value));
    }
}
