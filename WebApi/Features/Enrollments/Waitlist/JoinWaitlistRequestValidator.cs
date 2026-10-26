using FluentValidation;

namespace WebApi.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistRequestValidator : AbstractValidator<JoinWaitlistRequest>
{
    public JoinWaitlistRequestValidator()
    {
        RuleFor(request => request.ClassId).NotEqual(0L);
        RuleFor(request => request.Seats).GreaterThanOrEqualTo(1);
    }
}
