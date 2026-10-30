using System.Collections.Concurrent;

namespace Umbricians.LiveView.Messaging.Members;

/// <summary>
/// An optional in-memory store of messages waiting for each member, for senders that show messages the next time a
/// member views a page (a banner, say) rather than pushing them. A sender adds messages; the site's own view
/// component, partial view or endpoint reads them and marks them read, then renders them however it likes.
/// </summary>
/// <remarks>
/// Messages are kept until they expire, and at most the 200 most recent are held. Nothing is persisted, and on a
/// load-balanced site each server has its own inbox.
/// </remarks>
public interface IMemberMessageInbox
{
    void Add(MemberMessage message);

    /// <summary>Unexpired messages for the member that they have not marked read, oldest first.</summary>
    IReadOnlyList<MemberMessage> GetUnread(Guid memberKey);

    void MarkRead(Guid memberKey, Guid messageKey);
}

internal sealed class MemberMessageInbox : IMemberMessageInbox
{
    private const int MaxMessages = 200;

    private readonly ConcurrentDictionary<Guid, Entry> _messages = new();
    private readonly TimeProvider _timeProvider;

    public MemberMessageInbox(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public void Add(MemberMessage message)
    {
        Prune();

        // A null recipient set means every member.
        HashSet<Guid>? recipients = message.Audience == MemberMessageAudience.AllMembers ? null : [.. message.MemberKeys];
        _messages[message.Key] = new Entry(message, recipients);

        foreach (Entry oldest in _messages.Values.OrderBy(e => e.Message.SentAt).Take(Math.Max(0, _messages.Count - MaxMessages)))
        {
            _messages.TryRemove(oldest.Message.Key, out _);
        }
    }

    public IReadOnlyList<MemberMessage> GetUnread(Guid memberKey)
    {
        Prune();
        return _messages.Values
            .Where(e => (e.Recipients is null || e.Recipients.Contains(memberKey)) && !e.ReadBy.ContainsKey(memberKey))
            .Select(e => e.Message)
            .OrderBy(m => m.SentAt)
            .ToList();
    }

    public void MarkRead(Guid memberKey, Guid messageKey)
    {
        if (_messages.TryGetValue(messageKey, out Entry? entry))
        {
            entry.ReadBy.TryAdd(memberKey, 0);
        }
    }

    private void Prune()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((Guid key, Entry entry) in _messages)
        {
            if (entry.Message.ExpiresAt <= now)
            {
                _messages.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(MemberMessage Message, HashSet<Guid>? Recipients)
    {
        public ConcurrentDictionary<Guid, byte> ReadBy { get; } = new();
    }
}
