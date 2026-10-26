using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    public const string AdminRole = "admin";

    protected bool IsAdmin => User.IsInRole(AdminRole);

    protected bool TryGetCurrentUserId(out long userId)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdValue is null || !long.TryParse(userIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
        {
            userId = default;
            return false;
        }

        return true;
    }
}
