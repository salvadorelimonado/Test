using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.Cancel;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.Cancel;

public static class CancelEnrollmentResultToActionResultMapper
{
    public static IActionResult From(CancelEnrollmentResult result)
    {
        switch (result.Outcome)
        {
            case CancelEnrollmentOutcome.Succeeded:
                if (result.Response is null)
                {
                    throw new ArgumentNullException(nameof(result.Response));
                }

                return new ObjectResult(result.Response)
                {
                    StatusCode = StatusCodes.Status200OK
                };
            case CancelEnrollmentOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case CancelEnrollmentOutcome.Conflict:
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
