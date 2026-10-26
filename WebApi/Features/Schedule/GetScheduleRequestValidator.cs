using FluentValidation;
using BusinessLayer.Features.Schedule;

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
        RuleFor(request => request.ContinuationToken)
            .Must(token => string.IsNullOrWhiteSpace(token) || ScheduleContinuation.TryRead(token, out _, out _));
    }

    private static bool IsAfterFrom(DateTime? from, DateTime? to)
    {
        return from is DateTime fromInstant && to is DateTime toInstant && toInstant > fromInstant;
    }
}
