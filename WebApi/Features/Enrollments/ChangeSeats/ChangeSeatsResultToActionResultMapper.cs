using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.ChangeSeats;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.ChangeSeats;

public static class ChangeSeatsResultToActionResultMapper
{
    public static IActionResult From(ChangeSeatsResult result)
    {
        switch (result.Outcome)
        {
            case ChangeSeatsOutcome.Succeeded:
                if (result.Card is null)
                {
                    throw new ArgumentNullException(nameof(result.Card));
                }

                return new ObjectResult(result.Card)
                {
                    StatusCode = StatusCodes.Status200OK
                };
            case ChangeSeatsOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case ChangeSeatsOutcome.Conflict:
                if (result.Reason is null)
                {
                    throw new ArgumentNullException(nameof(result.Reason));
                }

                return ApiProblemsHelper.Create(StatusCodes.Status409Conflict, result.Reason);
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null);
        }
    }
}
