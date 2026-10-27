using BusinessLayer.Result;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Results;

public static class ObjectResultFactory
{
    public static IActionResult ToActionResult<T>(this OperationResult<T> result, int successStatusCode)
        where T : class
    {
        switch (result)
        {
            case OperationResult<T>.Ok ok:
                return new ObjectResult(ok.Value)
                {
                    StatusCode = successStatusCode
                };
            case OperationResult<T>.NotFound:
                return ApiProblemsHelper.Create(StatusCodes.Status404NotFound);
            case OperationResult<T>.Conflict conflict:
                return ApiProblemsHelper.Create(StatusCodes.Status409Conflict, conflict.Reason, conflict.Details);
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }
}
