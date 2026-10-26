using FluentValidation;
using WebApi.Features.Enrollments;

namespace WebApi.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsRequestValidator : AbstractValidator<ChangeSeatsRequest>
{
    public ChangeSeatsRequestValidator()
    {
        RuleFor(request => request.Seats).InclusiveBetween(1, EnrollmentSeatsLimits.MaxSeats);
    }
}
