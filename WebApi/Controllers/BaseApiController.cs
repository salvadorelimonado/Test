using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    public const string AdminRole = "admin";

    protected bool IsAdmin => User.IsInRole(AdminRole);

    protected long CurrentUserId
    {
        get
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdValue is null)
            {
                throw new ArgumentNullException(nameof(userIdValue));
            }

            return long.Parse(userIdValue, CultureInfo.InvariantCulture);
        }
    }
}
