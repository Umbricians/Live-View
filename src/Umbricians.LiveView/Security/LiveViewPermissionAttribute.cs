using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;

namespace Umbricians.LiveView.Security;

/// <summary>
/// Requires the current backoffice user to have a Live View permission through at least one of their user groups.
/// Apply alongside <c>AuthorizationPolicies.BackOfficeAccess</c>, which signs the user in; no section access is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class LiveViewPermissionAttribute : TypeFilterAttribute
{
    /// <param name="permission">One of <see cref="Constants.Permissions"/>.</param>
    public LiveViewPermissionAttribute(string permission)
        : base(typeof(LiveViewPermissionFilter))
    {
        Permission = permission;
        Arguments = [permission];
    }

    public string Permission { get; }

    private sealed class LiveViewPermissionFilter(string permission, IBackOfficeSecurityAccessor security) : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            IUser? user = security.BackOfficeSecurity?.CurrentUser;
            if (!LiveViewPermissions.Has(user, permission))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Missing Live View permission",
                    Detail = $"Your user group needs the '{permission}' permission.",
                })
                { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
    }
}

internal static class LiveViewPermissions
{
    /// <summary>Whether any of the user's groups grants the permission.</summary>
    public static bool Has(IUser? user, string permission) =>
        user?.Groups.Any(group => group.Permissions.Contains(permission)) == true;
}
