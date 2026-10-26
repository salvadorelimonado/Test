using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.Waitlist;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.Waitlist;

public static class JoinWaitlistResultToActionResultMapper
{
    public static IActionResult From(JoinWaitlistResult result)
    {
        switch (result.Outcome)
        {
            case JoinWaitlistOutcome.Created:
                if (result.Response is null)
                {
                    throw new ArgumentNullException(nameof(result.Response));
                }

                return new ObjectResult(result.Response)
                {
                    StatusCode = StatusCodes.Status201Created
                };
            case JoinWaitlistOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case JoinWaitlistOutcome.Conflict:
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
