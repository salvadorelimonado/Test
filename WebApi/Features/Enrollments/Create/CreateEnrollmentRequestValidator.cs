using FluentValidation;
using WebApi.Features.Enrollments;

namespace WebApi.Features.Enrollments.Create;

public sealed class CreateEnrollmentRequestValidator : AbstractValidator<CreateEnrollmentRequest>
{
    public CreateEnrollmentRequestValidator()
    {
        RuleFor(request => request.ClassId).NotEqual(0L);
        RuleFor(request => request.Seats).InclusiveBetween(1, EnrollmentSeatsLimits.MaxSeats);
    }
}
