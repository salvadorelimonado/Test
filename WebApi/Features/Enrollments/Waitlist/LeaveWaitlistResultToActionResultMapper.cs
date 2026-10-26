using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.Waitlist;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.Waitlist;

public static class LeaveWaitlistResultToActionResultMapper
{
    public static IActionResult From(LeaveWaitlistResult result)
    {
        switch (result.Outcome)
        {
            case LeaveWaitlistOutcome.Succeeded:
                if (result.Response is null)
                {
                    throw new ArgumentNullException(nameof(result.Response));
                }

                return new ObjectResult(result.Response)
                {
                    StatusCode = StatusCodes.Status200OK
                };
            case LeaveWaitlistOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case LeaveWaitlistOutcome.Conflict:
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
