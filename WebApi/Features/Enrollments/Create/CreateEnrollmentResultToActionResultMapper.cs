using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.Create;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.Create;

public static class CreateEnrollmentResultToActionResultMapper
{
    public static IActionResult From(CreateEnrollmentResult result)
    {
        switch (result.Outcome)
        {
            case CreateEnrollmentOutcome.Created:
                if (result.Response is null)
                {
                    throw new ArgumentNullException(nameof(result.Response));
                }

                return new ObjectResult(result.Response)
                {
                    StatusCode = StatusCodes.Status201Created
                };
            case CreateEnrollmentOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case CreateEnrollmentOutcome.Conflict:
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
