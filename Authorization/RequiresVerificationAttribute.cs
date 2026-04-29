using Beauty.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace Beauty.Api.Authorization;

/// <summary>
/// Blocks the action if the authenticated user is not fully verified (ID + address).
/// Apply to any endpoint that exposes artists to unverified customers.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresVerificationAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByIdAsync(userId);

        // Admins and employees are exempt
        var roles = await userManager.GetRolesAsync(user!);
        if (roles.Contains("Admin") || roles.Contains("Employee") || roles.Contains("Artist"))
        {
            await next();
            return;
        }

        if (user?.VerificationStatus != VerificationStatus.Verified)
        {
            context.Result = new ObjectResult(new
            {
                code    = "VERIFICATION_REQUIRED",
                message = "Identity verification is required to use this feature. Please verify your ID and proof of address.",
                status  = user?.VerificationStatus.ToString() ?? "Unverified",
            })
            { StatusCode = 403 };
            return;
        }

        await next();
    }
}
