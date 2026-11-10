using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Messaging;

/// <summary>
/// Writes every message sent to Umbraco's audit log, under the sending user, naming which delivery methods delivered a
/// member message and which failed.
/// </summary>
internal sealed class MessageAuditHandler :
    INotificationAsyncHandler<BackofficeMessageSentNotification>,
    INotificationAsyncHandler<MemberMessageSentNotification>
{
    private const string EntityType = "Umbricians.LiveView";

    private readonly IAuditService _auditService;
    private readonly ILogger<MessageAuditHandler> _logger;

    public MessageAuditHandler(IAuditService auditService, ILogger<MessageAuditHandler> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public Task HandleAsync(BackofficeMessageSentNotification notification, CancellationToken cancellationToken)
    {
        int count = notification.RecipientKeys.Count;
        string users = count == 1 ? "1 backoffice user" : $"{count} backoffice users";
        return AuditAsync(
            notification.SenderKey, notification.Message.Key, $"Sent a message to {users}: {notification.Message.Headline}");
    }

    public Task HandleAsync(MemberMessageSentNotification notification, CancellationToken cancellationToken)
    {
        MemberMessage message = notification.Message;
        int count = message.MemberKeys.Count;
        string audience = message.Audience switch
        {
            MemberMessageAudience.Selected => count == 1 ? "1 selected member" : $"{count} selected members",
            MemberMessageAudience.AllOnline => count == 1 ? "1 online member" : $"{count} online members",
            _ => "every member",
        };
        string[] delivered = notification.Results.Where(r => r.Error is null).Select(r => r.Name).ToArray();
        string failed = string.Join(
            ", ", notification.Results.Where(r => r.Error is not null).Select(r => $"{r.Name} {r.Error!.ToLowerInvariant()}"));
        string comment = delivered.Length == 0
            ? $"Message to {audience} not delivered ({failed}): {message.Headline}"
            : failed.Length == 0
                ? $"Sent a message to {audience} by {string.Join(", ", delivered)}: {message.Headline}"
                : $"Sent a message to {audience} by {string.Join(", ", delivered)} ({failed}): {message.Headline}";
        return AuditAsync(notification.SenderKey, message.Key, comment);
    }

    /// <summary>Never throws: the message has already gone, so a failed entry is only logged.</summary>
    private async Task AuditAsync(Guid? senderKey, Guid messageKey, string comment)
    {
        if (senderKey is null)
        {
            _logger.LogWarning("Message {MessageKey} was not audited: it was sent by no known backoffice user", messageKey);
            return;
        }

        try
        {
            Attempt<AuditLogOperationStatus> attempt = await _auditService.AddAsync(
                AuditType.Notify,
                senderKey.Value,
                Umbraco.Cms.Core.Constants.System.Root,
                EntityType,
                comment,
                messageKey.ToString());

            if (!attempt.Success)
            {
                _logger.LogWarning("Message {MessageKey} was not audited: {Status}", messageKey, attempt.Result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message {MessageKey} was not audited", messageKey);
        }
    }
}
