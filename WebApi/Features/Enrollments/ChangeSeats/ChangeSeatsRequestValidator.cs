using FluentValidation;

namespace WebApi.Features.Enrollments.ChangeSeats;

public sealed class ChangeSeatsRequestValidator : AbstractValidator<ChangeSeatsRequest>
{
    public ChangeSeatsRequestValidator()
    {
        RuleFor(request => request.Seats).GreaterThanOrEqualTo(1);
    }
}
