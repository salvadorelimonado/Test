using FluentValidation;

namespace WebApi.Features.Enrollments.Create;

public sealed class CreateEnrollmentRequestValidator : AbstractValidator<CreateEnrollmentRequest>
{
    public CreateEnrollmentRequestValidator()
    {
        RuleFor(request => request.Seats).GreaterThanOrEqualTo(1);
    }
}
