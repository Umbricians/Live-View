using System.Collections.Concurrent;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Messaging;

internal sealed record BackofficeMessage(
    Guid Key,
    string Headline,
    string Message,
    string Color,
    string? SenderName,
    DateTimeOffset SentAt);

/// <summary>A message as the dashboard sends it: text untrimmed, colour unchecked, recipients not yet worked out.</summary>
internal sealed record BackofficeMessageRequest(
    string? Headline,
    string? Message,
    string? Color,
    IReadOnlyCollection<Guid>? UserKeys,
    bool AllOnline);

/// <summary>The backoffice user sending a message, who is never one of its recipients.</summary>
internal sealed record BackofficeSender(Guid? Key, string? Name);

internal abstract record BackofficeSendResult
{
    private BackofficeSendResult()
    {
    }

    internal sealed record Sent(BackofficeMessage Message, int Recipients) : BackofficeSendResult;

    internal sealed record Rejected(string Reason) : BackofficeSendResult;
}

/// <summary>
/// Sends a one-off notification to chosen backoffice users, e.g. to warn them before an upgrade or restart.
/// </summary>
/// <remarks>
/// It owns the send rules: the message content (see <see cref="MessageContent"/>), who "all online" means at the
/// moment of sending, and that the sender is never a recipient. Selected users are sent to as chosen, even if they have since gone
/// offline, and pick the message up if they come back within its lifetime.
/// Server events only carry a key, so the message is held in memory and each recipient's backoffice fetches it
/// by key. Recipients whose realtime connection is down pick it up from their next heartbeat instead.
/// Messages are discarded after <see cref="Lifetime"/>.
/// </remarks>
internal sealed class BackofficeMessenger
{
    public const string EventSource = "Umbricians.LiveView";
    public const string EventType = "Message";

    internal const int MaxRecipients = 1000;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, Entry> _messages = new();
    private readonly IServerEventRouter _router;
    private readonly BackofficePresence _presence;
    private readonly TimeProvider _timeProvider;

    public BackofficeMessenger(IServerEventRouter router, BackofficePresence presence, TimeProvider timeProvider)
    {
        _router = router;
        _presence = presence;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// How many users a message to everyone online would reach right now: everyone in the backoffice but the sender.
    /// Users only on MCP are left out, since the message is shown in a backoffice tab.
    /// </summary>
    internal int CountAllOnlineRecipients(Guid? senderKey) => _presence.AllInBackoffice().Count(k => k != senderKey);

    /// <summary>Checks the message, works out its recipients and delivers it, or says why it can't be sent.</summary>
    internal async Task<BackofficeSendResult> SendAsync(BackofficeMessageRequest request, BackofficeSender sender)
    {
        if (!MessageContent.TryCreate(request.Headline, request.Message, request.Color, out MessageContent content))
        {
            return new BackofficeSendResult.Rejected(MessageContent.Limits);
        }

        // "All online" is resolved here rather than in the browser, which only ever holds one page of users.
        Guid[] recipients = (request.AllOnline ? _presence.AllInBackoffice() : request.UserKeys ?? [])
            .Where(k => k != sender.Key)
            .Distinct()
            .ToArray();

        if (recipients.Length is 0 or > MaxRecipients)
        {
            return new BackofficeSendResult.Rejected($"Choose between 1 and {MaxRecipients} other users to send to.");
        }

        Prune();

        var sent = new BackofficeMessage(
            Guid.NewGuid(), content.Headline, content.Message, content.Color, sender.Name, _timeProvider.GetUtcNow());
        _messages[sent.Key] = new Entry(sent, new ConcurrentDictionary<Guid, bool>(recipients.Select(r => KeyValuePair.Create(r, false))));

        var serverEvent = new ServerEvent { EventSource = EventSource, EventType = EventType, Key = sent.Key };
        await Task.WhenAll(recipients.Select(r => _router.NotifyUserAsync(serverEvent, r)));

        return new BackofficeSendResult.Sent(sent, recipients.Length);
    }

    /// <summary>Returns the message if the user is a recipient, and marks it delivered to them.</summary>
    public BackofficeMessage? Receive(Guid messageKey, Guid userKey)
    {
        if (!_messages.TryGetValue(messageKey, out Entry? entry) || !entry.Recipients.ContainsKey(userKey))
        {
            return null;
        }

        entry.Recipients[userKey] = true;
        return entry.Message;
    }

    /// <summary>Keys of messages sent to the user that their backoffice has not fetched yet.</summary>
    public IReadOnlyList<Guid> GetUndelivered(Guid userKey)
    {
        Prune();
        return _messages.Values
            .Where(e => e.Recipients.TryGetValue(userKey, out bool delivered) && !delivered)
            .Select(e => e.Message.Key)
            .ToList();
    }

    private void Prune()
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - Lifetime;
        foreach ((Guid key, Entry entry) in _messages)
        {
            if (entry.Message.SentAt < cutoff)
            {
                _messages.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(BackofficeMessage Message, ConcurrentDictionary<Guid, bool> Recipients);
}
