using Microsoft.AspNetCore.Mvc;
using BusinessLayer.Features.Enrollments.Get;
using WebApi.Helpers;

namespace WebApi.Features.Enrollments.Get;

public static class GetEnrollmentResultToActionResultMapper
{
    public static IActionResult From(GetEnrollmentResult result)
    {
        switch (result.Outcome)
        {
            case GetEnrollmentOutcome.Found:
                if (result.Card is null)
                {
                    throw new ArgumentNullException(nameof(result.Card));
                }

                return new ObjectResult(result.Card)
                {
                    StatusCode = StatusCodes.Status200OK
                };
            case GetEnrollmentOutcome.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null);
        }
    }
}
