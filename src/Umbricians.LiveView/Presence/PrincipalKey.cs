using System.Security.Claims;
using Umbraco.Extensions;

namespace Umbricians.LiveView.Presence;

internal static class PrincipalKey
{
    /// <summary>
    /// The signed-in person's key. It is in the "sub" claim when there is one; otherwise the name identifier holds the
    /// key, or a member's numeric id, which <paramref name="findMemberKey"/> turns into the key.
    /// </summary>
    public static Guid? Resolve(ClaimsPrincipal user, Func<int, Guid?> findMemberKey)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (user.Identity.GetUserKey() is { } key)
        {
            return key;
        }

        string? id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(id, out Guid parsed))
        {
            return parsed;
        }

        return int.TryParse(id, out int memberId) ? findMemberKey(memberId) : null;
    }
}
