using System.Text.Json;
using BusinessLayer.Result;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApi.Helpers;

public static class ApiProblemsHelper
{
    public static ObjectResult Create(int statusCode, ConflictReason? reason = null)
    {
        var details = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode)
        };

        if (reason is ConflictReason value)
        {
            details.Extensions["reason"] = JsonSerializer.SerializeToElement(value).GetString()
                ?? throw new ArgumentNullException(nameof(reason));
        }

        return new ObjectResult(details)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
