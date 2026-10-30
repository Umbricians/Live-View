using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Presence;

/// <summary>Takes deleted backoffice users off the dashboard straight away.</summary>
internal sealed class BackofficeUserDeletedHandler : INotificationHandler<UserDeletedNotification>
{
    private readonly BackofficePresence _presence;

    public BackofficeUserDeletedHandler(BackofficePresence presence) => _presence = presence;

    public void Handle(UserDeletedNotification notification)
    {
        foreach (IUser user in notification.DeletedEntities)
        {
            _presence.Forget(user.Key);
        }
    }
}

/// <summary>Takes backoffice users off the dashboard straight away when they are disabled.</summary>
internal sealed class BackofficeUserDisabledHandler : INotificationHandler<UserSavedNotification>
{
    private readonly BackofficePresence _presence;

    public BackofficeUserDisabledHandler(BackofficePresence presence) => _presence = presence;

    public void Handle(UserSavedNotification notification)
    {
        foreach (IUser user in notification.SavedEntities.Where(u => u.UserState == UserState.Disabled))
        {
            _presence.Forget(user.Key);
        }
    }
}
