using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Looks up members recorded by <see cref="MemberPresence"/>, which holds a member's key where their claims carry
/// one and their id otherwise. Each kind is fetched in a single batch.
/// </summary>
public sealed class MemberResolver
{
    private readonly IMemberService _memberService;

    public MemberResolver(IMemberService memberService) => _memberService = memberService;

    /// <summary>The members found, keyed by the id they were recorded under.</summary>
    public async Task<IReadOnlyDictionary<string, IMember>> ResolveAsync(IEnumerable<string> memberIds)
    {
        string[] all = memberIds.Distinct().ToArray();
        var found = new Dictionary<string, IMember>();

        Guid[] keys = all.Select(id => Guid.TryParse(id, out Guid key) ? key : Guid.Empty).Where(k => k != Guid.Empty).ToArray();
        if (keys.Length > 0)
        {
            foreach (IMember member in await _memberService.GetByKeysAsync(keys))
            {
                found[member.Key.ToString()] = member;
            }
        }

        int[] ids = all.Select(id => int.TryParse(id, out int i) ? i : 0).Where(i => i > 0).ToArray();
        if (ids.Length > 0)
        {
            foreach (IMember member in _memberService.GetAllMembers(ids))
            {
                found[member.Id.ToString()] = member;
            }
        }

        return found;
    }
}
