using Umbraco.Cms.Core.Models;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// Gives demo members one of a few pictures bundled with the demo, and leaves every fourth member without one so the
/// initials fallback shows too. Demo only: a real site would read a member property, Gravatar or a profile service.
/// </summary>
public class DemoMemberAvatarProvider : IMemberAvatarProvider
{
    private static readonly string?[] Avatars =
    [
        "/demo-avatars/sunrise.svg",
        "/demo-avatars/ocean.svg",
        "/demo-avatars/forest.svg",
        null,
    ];

    public Task<string?> GetAvatarUrlAsync(IMember member, CancellationToken cancellationToken) =>
        Task.FromResult(Avatars[(member.Email ?? member.Username).Sum(c => c) % Avatars.Length]);
}
