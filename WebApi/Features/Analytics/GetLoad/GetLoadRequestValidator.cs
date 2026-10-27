using FluentValidation;

namespace WebApi.Features.Analytics.GetLoad;

public sealed class GetLoadRequestValidator : AbstractValidator<GetLoadRequest>
{
    public GetLoadRequestValidator()
    {
        RuleFor(request => request.From).NotNull();
        RuleFor(request => request.To).NotNull();
        RuleFor(request => request.To)
            .Must((request, to) => IsOnOrAfterFrom(request.From, to))
            .When(request => request.From is not null && request.To is not null);
    }

    private static bool IsOnOrAfterFrom(DateOnly? from, DateOnly? to)
    {
        return from is DateOnly fromDate && to is DateOnly toDate && toDate >= fromDate;
    }
}
