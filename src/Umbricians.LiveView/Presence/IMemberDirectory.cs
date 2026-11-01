using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbricians.LiveView.Presence;

/// <param name="AvatarUrl">The member's picture from the site's <see cref="IMemberAvatarProvider"/>, or null for none.</param>
internal sealed record MemberDetails(Guid Key, string Name, string? Email, string? MemberType, string? AvatarUrl);

/// <summary>
/// Where <see cref="MemberPresence"/> looks members up. Umbraco's member store on a site, an in-memory list in tests.
/// </summary>
internal interface IMemberDirectory
{
    /// <summary>The key of the member with this numeric id, or null when there is no such member.</summary>
    Guid? FindKey(int memberId);

    /// <summary>The members that still exist among <paramref name="keys"/>, in one batch.</summary>
    Task<IReadOnlyList<MemberDetails>> GetAsync(IReadOnlyCollection<Guid> keys);
}

internal sealed class UmbracoMemberDirectory : IMemberDirectory
{
    private static readonly TimeSpan AvatarTimeout = TimeSpan.FromSeconds(2);

    private readonly IIdKeyMap _idKeyMap;
    private readonly IMemberService _memberService;
    private readonly IMemberAvatarProvider _avatars;
    private readonly ILogger<UmbracoMemberDirectory> _logger;

    public UmbracoMemberDirectory(
        IIdKeyMap idKeyMap,
        IMemberService memberService,
        IMemberAvatarProvider avatars,
        ILogger<UmbracoMemberDirectory> logger)
    {
        _idKeyMap = idKeyMap;
        _memberService = memberService;
        _avatars = avatars;
        _logger = logger;
    }

    // The id/key map keeps its own cache, so this only reaches the database the first time a member is seen.
    public Guid? FindKey(int memberId) =>
        _idKeyMap.GetKeyForId(memberId, UmbracoObjectTypes.Member) is { Success: true } attempt ? attempt.Result : null;

    public async Task<IReadOnlyList<MemberDetails>> GetAsync(IReadOnlyCollection<Guid> keys)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        IEnumerable<IMember> members = await _memberService.GetByKeysAsync(keys.ToArray());
        return await Task.WhenAll(members.Select(async m => new MemberDetails(
            m.Key,
            m.Name ?? m.Username,
            m.Email,
            m.ContentType.Name ?? m.ContentTypeAlias,
            await AvatarLookup.TryAsync(token => _avatars.GetAvatarUrlAsync(m, token), AvatarTimeout, _logger, m.Key))));
    }
}
