using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbricians.LiveView.Messaging;

/// <summary>
/// Published before a message to backoffice users is delivered. Call <c>CancelOperation</c> with an
/// <see cref="EventMessage"/> to stop it; the dashboard shows the <see cref="EventMessage"/>'s message text as the reason.
/// </summary>
public sealed class BackofficeMessageSendingNotification : CancelableNotification
{
    /// <summary>Creates the notification for a message about to be sent.</summary>
    /// <param name="message">The message about to be sent.</param>
    /// <param name="recipientKeys">The users it will go to.</param>
    /// <param name="senderKey">The backoffice user sending it.</param>
    /// <param name="messages">Collects the reason when a handler cancels the message.</param>
    public BackofficeMessageSendingNotification(
        BackofficeMessage message, IReadOnlyList<Guid> recipientKeys, Guid? senderKey, EventMessages messages)
        : base(messages)
    {
        Message = message;
        RecipientKeys = recipientKeys;
        SenderKey = senderKey;
    }

    /// <summary>The message about to be sent.</summary>
    public BackofficeMessage Message { get; }

    /// <summary>The users it will go to: "all online" already resolved and the sender left out.</summary>
    public IReadOnlyList<Guid> RecipientKeys { get; }

    /// <summary>The backoffice user sending it; their name is <see cref="BackofficeMessage.SenderName"/>.</summary>
    public Guid? SenderKey { get; }
}

/// <summary>
/// Published after a message has been pushed to each recipient's backoffice. Recipients without a realtime connection
/// pick it up from their next heartbeat.
/// </summary>
public sealed class BackofficeMessageSentNotification : INotification
{
    /// <summary>Creates the notification for a message that has been sent.</summary>
    /// <param name="message">The message that was sent.</param>
    /// <param name="recipientKeys">The users it went to.</param>
    /// <param name="senderKey">The backoffice user who sent it.</param>
    public BackofficeMessageSentNotification(BackofficeMessage message, IReadOnlyList<Guid> recipientKeys, Guid? senderKey)
    {
        Message = message;
        RecipientKeys = recipientKeys;
        SenderKey = senderKey;
    }

    /// <summary>The message that was sent.</summary>
    public BackofficeMessage Message { get; }

    /// <summary>The users it went to: "all online" already resolved and the sender left out.</summary>
    public IReadOnlyList<Guid> RecipientKeys { get; }

    /// <summary>The backoffice user who sent it; their name is <see cref="BackofficeMessage.SenderName"/>.</summary>
    public Guid? SenderKey { get; }
}
