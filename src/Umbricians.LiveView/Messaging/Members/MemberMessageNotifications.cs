using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Messaging.Members;

/// <summary>
/// Published before a member message is handed to its delivery methods. Call <c>CancelOperation</c> with an
/// <see cref="EventMessage"/> to stop it; the dashboard shows the <see cref="EventMessage"/>'s message text as the reason.
/// </summary>
public sealed class MemberMessageSendingNotification : CancelableNotification
{
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
