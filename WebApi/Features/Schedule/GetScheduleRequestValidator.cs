using FluentValidation;

namespace WebApi.Features.Schedule;

public sealed class GetScheduleRequestValidator : AbstractValidator<GetScheduleRequest>
{
    public GetScheduleRequestValidator()
    {
        RuleFor(request => request.From).NotNull();
        RuleFor(request => request.To).NotNull();
        RuleFor(request => request.To)
            .Must((request, to) => IsAfterFrom(request.From, to))
            .When(request => request.From is not null && request.To is not null);
        RuleFor(request => request.Limit)
            .Must(limit => limit is null || (limit >= SchedulePaginationLimits.MinPageSize && limit <= SchedulePaginationLimits.MaxPageSize));
    }

    private static bool IsAfterFrom(DateTime? from, DateTime? to)
    {
        return from is DateTime fromInstant && to is DateTime toInstant && toInstant > fromInstant;
    }
}
