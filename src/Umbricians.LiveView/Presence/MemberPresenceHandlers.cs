using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Takes a member off the dashboard as soon as they log out, rather than when their activity window runs out.
/// </summary>
internal sealed class MemberLogoutHandler : INotificationAsyncHandler<MemberLogoutSuccessNotification>
{
    private readonly MemberPresence _presence;

    public MemberLogoutHandler(MemberPresence presence) => _presence = presence;

    public async Task HandleAsync(MemberLogoutSuccessNotification notification, CancellationToken cancellationToken)
    {
        if (notification.MemberKey is { } key)
        {
            await _presence.SignedOutAsync(key);
        }
    }
}

/// <summary>Takes deleted members off the dashboard straight away.</summary>
internal sealed class MemberDeletedHandler : INotificationAsyncHandler<MemberDeletedNotification>
{
    private readonly MemberPresence _presence;

    public MemberDeletedHandler(MemberPresence presence) => _presence = presence;

    public async Task HandleAsync(MemberDeletedNotification notification, CancellationToken cancellationToken)
    {
        foreach (IMember member in notification.DeletedEntities)
        {
            await _presence.ForgetAsync(member.Key);
        }
    }
}
