using System.Collections.Concurrent;

namespace Umbricians.LiveView.Messaging;

internal sealed class InMemoryBackofficeMessageStore : IBackofficeMessageStore
{
    private readonly ConcurrentDictionary<Guid, Entry> _messages = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryBackofficeMessageStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task AddAsync(BackofficeMessage message, IReadOnlyCollection<Guid> recipients, DateTimeOffset expiresAt)
    {
        Prune();
        _messages[message.Key] = new Entry(
            message, expiresAt, new ConcurrentDictionary<Guid, bool>(recipients.Select(r => KeyValuePair.Create(r, false))));
        return Task.CompletedTask;
    }

    public Task<BackofficeMessage?> ReceiveAsync(Guid messageKey, Guid userKey)
    {
        if (!_messages.TryGetValue(messageKey, out Entry? entry) || !entry.Recipients.ContainsKey(userKey))
        {
            return Task.FromResult<BackofficeMessage?>(null);
        }

        entry.Recipients[userKey] = true;
        return Task.FromResult<BackofficeMessage?>(entry.Message);
    }

    public Task<IReadOnlyList<Guid>> GetUndeliveredAsync(Guid userKey)
    {
        Prune();
        IReadOnlyList<Guid> undelivered = _messages.Values
            .Where(e => e.Recipients.TryGetValue(userKey, out bool delivered) && !delivered)
            .Select(e => e.Message.Key)
            .ToList();
        return Task.FromResult(undelivered);
    }

    private void Prune()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((Guid key, Entry entry) in _messages)
        {
            if (entry.ExpiresAt < now)
            {
                _messages.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(BackofficeMessage Message, DateTimeOffset ExpiresAt, ConcurrentDictionary<Guid, bool> Recipients);
}
