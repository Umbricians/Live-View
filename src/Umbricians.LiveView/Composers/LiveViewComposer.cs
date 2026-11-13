using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Composers;

/// <summary>
/// Registers Live View with Umbraco.
/// </summary>
public class LiveViewComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddLiveView(builder.Config);

        builder.AddNotificationAsyncHandler<UserDeletedNotification, BackofficeUserDeletedHandler>();
        builder.AddNotificationAsyncHandler<UserSavedNotification, BackofficeUserDisabledHandler>();
        builder.AddNotificationAsyncHandler<MemberLogoutSuccessNotification, MemberLogoutHandler>();
        builder.AddNotificationAsyncHandler<MemberDeletedNotification, MemberDeletedHandler>();
        builder.AddNotificationAsyncHandler<BackofficeMessageSentNotification, MessageAuditHandler>();
        builder.AddNotificationAsyncHandler<MemberMessageSentNotification, MessageAuditHandler>();
    }
}
