using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Core.Services;

namespace Umbricians.LiveView.Presence;

/// <param name="Connections">How many realtime (server events) connections the user has open, one per tab.</param>
internal sealed record BackofficeUserDetails(Guid Key, string Name, string? Email, int Connections);

/// <summary>
/// Where <see cref="BackofficePresence"/> looks users up. Umbraco's user store and realtime connections on a site, an
/// in-memory list in tests.
/// </summary>
internal interface IBackofficeUserDirectory
{
    /// <summary>The users among <paramref name="keys"/> that still exist and are not disabled, in one batch.</summary>
    Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys);
}

internal sealed class UmbracoBackofficeUserDirectory : IBackofficeUserDirectory
{
    private readonly IUserService _userService;
    private readonly IUserConnectionManager _connections;

    public UmbracoBackofficeUserDirectory(IUserService userService, IUserConnectionManager connections)
    {
        _userService = userService;
        _connections = connections;
    }

    public async Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        IEnumerable<IUser> users = await _userService.GetAsync(keys);
        return users
            .Where(u => u.UserState != UserState.Disabled)
            .Select(u => new BackofficeUserDetails(u.Key, u.Name ?? u.Username, u.Email, _connections.GetConnections(u.Key).Count))
            .ToList();
    }
}
