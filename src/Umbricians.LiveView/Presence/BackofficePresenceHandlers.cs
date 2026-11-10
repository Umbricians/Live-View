using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Presence;

/// <summary>Takes deleted backoffice users off the dashboard straight away.</summary>
internal sealed class BackofficeUserDeletedHandler : INotificationAsyncHandler<UserDeletedNotification>
{
    private readonly BackofficePresence _presence;

    public BackofficeUserDeletedHandler(BackofficePresence presence) => _presence = presence;

    public async Task HandleAsync(UserDeletedNotification notification, CancellationToken cancellationToken)
    {
        foreach (IUser user in notification.DeletedEntities)
        {
            await _presence.ForgetAsync(user.Key);
        }
    }
}

/// <summary>Takes backoffice users off the dashboard straight away when they are disabled.</summary>
internal sealed class BackofficeUserDisabledHandler : INotificationAsyncHandler<UserSavedNotification>
{
    private readonly BackofficePresence _presence;

    public BackofficeUserDisabledHandler(BackofficePresence presence) => _presence = presence;

    public async Task HandleAsync(UserSavedNotification notification, CancellationToken cancellationToken)
    {
        foreach (IUser user in notification.SavedEntities.Where(u => u.UserState == UserState.Disabled))
        {
            await _presence.ForgetAsync(user.Key);
        }
    }
}
