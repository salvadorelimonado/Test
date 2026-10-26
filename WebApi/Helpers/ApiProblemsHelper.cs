using BusinessLayer.Result;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApi.Helpers;

public static class ApiProblemsHelper
{
    public static ObjectResult Create(
        int statusCode,
        ConflictReason? reason = null,
        IReadOnlyDictionary<string, object>? details = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode)
        };

        if (reason is ConflictReason value)
        {
            problem.Extensions["reason"] = value;
        }

        if (details is not null)
        {
            foreach (var detail in details)
            {
                problem.Extensions[detail.Key] = detail.Value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
