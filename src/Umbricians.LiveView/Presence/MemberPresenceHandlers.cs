using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Takes a member off the dashboard as soon as they log out, rather than when their activity window runs out.
/// </summary>
internal sealed class MemberLogoutHandler : INotificationHandler<MemberLogoutSuccessNotification>
{
    private readonly MemberPresence _presence;

    public MemberLogoutHandler(MemberPresence presence) => _presence = presence;

    public void Handle(MemberLogoutSuccessNotification notification)
    {
        if (notification.MemberKey is { } key)
        {
            _presence.SignedOut(key);
        }
    }
}

/// <summary>Takes deleted members off the dashboard straight away.</summary>
internal sealed class MemberDeletedHandler : INotificationHandler<MemberDeletedNotification>
{
    private readonly MemberPresence _presence;

    public MemberDeletedHandler(MemberPresence presence) => _presence = presence;

    public void Handle(MemberDeletedNotification notification)
    {
        foreach (IMember member in notification.DeletedEntities)
        {
            _presence.Forget(member.Key);
        }
    }
}
