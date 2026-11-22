using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Core.Services;

namespace Umbricians.LiveView.Presence;

/// <summary>A backoffice user as <see cref="IBackofficeUserDirectory"/> finds them.</summary>
/// <param name="Key">The user's key.</param>
/// <param name="Name">The user's name, or their username when they have no name.</param>
/// <param name="Email">The user's email address, or null when they have none.</param>
/// <param name="Connections">How many realtime (server events) connections the user has open, one per tab.</param>
/// <param name="AvatarUrls">The user's avatar in Umbraco's sizes, smallest first, or none when they have not set one.</param>
/// <param name="UserGroupKeys">The keys of the user groups the user is in.</param>
internal sealed record BackofficeUserDetails(
    Guid Key,
    string Name,
    string? Email,
    int Connections,
    IReadOnlyList<string> AvatarUrls,
    IReadOnlyCollection<Guid> UserGroupKeys);

/// <summary>
/// Where Live View looks backoffice users and user groups up. Umbraco's user services and realtime connections on a
/// site, an in-memory list in tests.
/// </summary>
internal interface IBackofficeUserDirectory
{
    /// <summary>The users among <paramref name="keys"/> that still exist and are not disabled, in one batch.</summary>
    Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys);

    /// <summary>The user groups among <paramref name="keys"/> that exist, in one batch.</summary>
    Task<IReadOnlyCollection<Guid>> FindUserGroupsAsync(IReadOnlyCollection<Guid> keys);
}

internal sealed class UmbracoBackofficeUserDirectory : IBackofficeUserDirectory
{
    private readonly IUserService _userService;
    private readonly IUserGroupService _userGroupService;
    private readonly IUserConnectionManager _connections;
    private readonly AppCaches _appCaches;
    private readonly MediaFileManager _mediaFileManager;
    private readonly IImageUrlGenerator _imageUrlGenerator;

    public UmbracoBackofficeUserDirectory(
        IUserService userService,
        IUserGroupService userGroupService,
        IUserConnectionManager connections,
        AppCaches appCaches,
        MediaFileManager mediaFileManager,
        IImageUrlGenerator imageUrlGenerator)
    {
        _userService = userService;
        _userGroupService = userGroupService;
        _connections = connections;
        _appCaches = appCaches;
        _mediaFileManager = mediaFileManager;
        _imageUrlGenerator = imageUrlGenerator;
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
            .Select(u => new BackofficeUserDetails(
                u.Key,
                u.Name ?? u.Username,
                u.Email,
                _connections.GetConnections(u.Key).Count,
                u.GetUserAvatarUrls(_appCaches.RuntimeCache, _mediaFileManager, _imageUrlGenerator),
                u.Groups.Select(g => g.Key).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyCollection<Guid>> FindUserGroupsAsync(IReadOnlyCollection<Guid> keys)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        return (await _userGroupService.GetAsync(keys)).Select(g => g.Key).ToList();
    }
}
