using FluentValidation;
using WebApi.Features.Enrollments;

namespace WebApi.Features.Enrollments.Waitlist;

public sealed class JoinWaitlistRequestValidator : AbstractValidator<JoinWaitlistRequest>
{
    public JoinWaitlistRequestValidator()
    {
        RuleFor(request => request.ClassId).NotEqual(0L);
        RuleFor(request => request.Seats).InclusiveBetween(1, EnrollmentSeatsLimits.MaxSeats);
    }
}
