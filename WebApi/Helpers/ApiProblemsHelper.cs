using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApi.Helpers;

public static class ApiProblemsHelper
{
    public static ObjectResult Create(int statusCode, string? reason = null)
    {
        var details = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode)
        };

        if (reason is not null)
        {
            details.Extensions["reason"] = reason;
        }

        return new ObjectResult(details)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
