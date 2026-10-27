using BusinessLayer.Result;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.ActionResults;

public static class OperationResultExtensions
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
                return ProblemResultFactory.Create(StatusCodes.Status404NotFound);
            case OperationResult<T>.Conflict conflict:
                return ProblemResultFactory.Create(StatusCodes.Status409Conflict, conflict.Reason, conflict.Details);
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }
}
