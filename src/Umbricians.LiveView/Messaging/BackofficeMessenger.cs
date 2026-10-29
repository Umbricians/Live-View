using System.Collections.Concurrent;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;

namespace Umbricians.LiveView.Messaging;

public sealed record BackofficeMessage(
    Guid Key,
    string Headline,
    string Message,
    string Color,
    string? SenderName,
    DateTimeOffset SentAt);

/// <summary>
/// Sends a one-off notification to chosen backoffice users, e.g. to warn them before an upgrade or restart.
/// </summary>
/// <remarks>
/// Server events only carry a key, so the message is held in memory and each recipient's backoffice fetches it
/// by key. Recipients whose realtime connection is down pick it up from their next heartbeat instead.
/// Messages are discarded after <see cref="Lifetime"/>.
/// </remarks>
public sealed class BackofficeMessenger
{
    public const string EventSource = "Umbricians.LiveView";
    public const string EventType = "Message";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, Entry> _messages = new();
    private readonly IServerEventRouter _router;
    private readonly TimeProvider _timeProvider;

    public BackofficeMessenger(IServerEventRouter router, TimeProvider timeProvider)
    {
        _router = router;
        _timeProvider = timeProvider;
    }

    public async Task<BackofficeMessage> SendAsync(
        string headline,
        string message,
        string color,
        string? senderName,
        IReadOnlyCollection<Guid> recipients)
    {
        Prune();

        var sent = new BackofficeMessage(Guid.NewGuid(), headline, message, color, senderName, _timeProvider.GetUtcNow());
        _messages[sent.Key] = new Entry(sent, new ConcurrentDictionary<Guid, bool>(recipients.Select(r => KeyValuePair.Create(r, false))));

        var serverEvent = new ServerEvent { EventSource = EventSource, EventType = EventType, Key = sent.Key };
        await Task.WhenAll(recipients.Select(r => _router.NotifyUserAsync(serverEvent, r)));

        return sent;
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
