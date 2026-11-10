using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Messaging.Members;

/// <summary>
/// Published before a member message is handed to its delivery methods. Call <c>CancelOperation</c> with an
/// <see cref="EventMessage"/> to stop it; the dashboard shows the <see cref="EventMessage"/>'s message text as the reason.
/// </summary>
public sealed class MemberMessageSendingNotification : CancelableNotification
{
    /// <summary>Creates the notification for a member message about to be sent.</summary>
    /// <param name="message">The message about to be sent.</param>
    /// <param name="deliveryMethods">The aliases of the senders chosen to deliver it.</param>
    /// <param name="senderKey">The backoffice user sending it.</param>
    /// <param name="messages">Collects the reason when a handler cancels the message.</param>
    public MemberMessageSendingNotification(
        MemberMessage message, IReadOnlyList<string> deliveryMethods, Guid? senderKey, EventMessages messages)
        : base(messages)
    {
        Message = message;
        DeliveryMethods = deliveryMethods;
        SenderKey = senderKey;
    }

    /// <summary>The message about to be sent. Its recipients are in <see cref="MemberMessage.Audience"/> and <see cref="MemberMessage.MemberKeys"/>.</summary>
    public MemberMessage Message { get; }

    /// <summary>The aliases of the <see cref="IMemberMessageSender"/>s chosen to deliver it.</summary>
    public IReadOnlyList<string> DeliveryMethods { get; }

    /// <summary>The backoffice user sending it; their name is <see cref="MemberMessage.SenderName"/>.</summary>
    public Guid? SenderKey { get; }
}

/// <summary>Published after every chosen delivery method has finished with a member message, whether or not it delivered.</summary>
public sealed class MemberMessageSentNotification : INotification
{
    /// <summary>Creates the notification for a member message every chosen delivery method has finished with.</summary>
    /// <param name="message">The message that was sent.</param>
    /// <param name="results">What each delivery method did.</param>
    /// <param name="senderKey">The backoffice user who sent it.</param>
    public MemberMessageSentNotification(
        MemberMessage message, IReadOnlyList<MemberMessageSenderResult> results, Guid? senderKey)
    {
        Message = message;
        Results = results;
        SenderKey = senderKey;
    }

    /// <summary>The message that was sent. Its recipients are in <see cref="MemberMessage.Audience"/> and <see cref="MemberMessage.MemberKeys"/>.</summary>
    public MemberMessage Message { get; }

    /// <summary>What each delivery method did, one per method, including failures and timeouts.</summary>
    public IReadOnlyList<MemberMessageSenderResult> Results { get; }

    /// <summary>The backoffice user who sent it; their name is <see cref="MemberMessage.SenderName"/>.</summary>
    public Guid? SenderKey { get; }
}
