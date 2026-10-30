using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

public sealed record OnlineMember(string MemberId, DateTimeOffset LastSeen);

/// <summary>
/// Remembers when each logged-in member last made a front-end request, so the dashboard can list who is online.
/// </summary>
public sealed class MemberPresence
{
    private readonly ConcurrentDictionary<string, Seen> _members = new();
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public MemberPresence(IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <param name="memberId">The member's key, or its id when the key is not in the member's claims.</param>
    public void Record(string memberId)
    {
        long now = _timeProvider.GetUtcNow().UtcTicks;
        _members.AddOrUpdate(memberId, static (_, n) => new Seen(n, n), static (_, seen, n) => seen with { Last = n }, now);
    }

    /// <summary>Forgets a member straight away, e.g. when they log out.</summary>
    public void Remove(string memberId) => _members.TryRemove(memberId, out _);

    /// <summary>
    /// One page of members seen within the members channel's activity window, newest arrivals first, plus how many
    /// there are in total. Ordering by arrival rather than activity keeps rows from jumping between pages.
    /// </summary>
    public (int Total, IReadOnlyList<OnlineMember> Page) GetOnline(int skip, int take)
    {
        long cutoff = (_timeProvider.GetUtcNow() - Window).UtcTicks;
        var online = new List<(string Id, Seen Seen)>(_members.Count);
        foreach ((string id, Seen seen) in _members)
        {
            if (seen.Last < cutoff)
            {
                _members.TryRemove(new KeyValuePair<string, Seen>(id, seen));
            }
            else
            {
                online.Add((id, seen));
            }
        }

        IReadOnlyList<OnlineMember> page = online
            .OrderByDescending(m => m.Seen.First)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .Select(m => new OnlineMember(m.Id, new DateTimeOffset(m.Seen.Last, TimeSpan.Zero)))
            .ToList();

        return (online.Count, page);
    }

    /// <summary>Everyone currently online, e.g. to address a message to all of them.</summary>
    public IReadOnlyList<string> GetAllOnlineIds()
    {
        (int total, _) = GetOnline(0, 0);
        return GetOnline(0, Math.Max(total, 1)).Page.Select(m => m.MemberId).ToList();
    }

    private sealed record Seen(long First, long Last);

    private TimeSpan Window
    {
        get
        {
            LiveViewOptions options = _options.CurrentValue;
            int? channelWindow = options.Channels.TryGetValue(Constants.Channels.Members, out LiveViewChannelOptions? channel)
                ? channel.ActivityWindowSeconds
                : null;
            return TimeSpan.FromSeconds(channelWindow ?? options.ActivityWindowSeconds);
        }
    }
}
