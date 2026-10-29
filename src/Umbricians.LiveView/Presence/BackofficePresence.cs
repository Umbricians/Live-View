using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

public sealed record OnlineBackofficeUser(Guid UserKey, DateTimeOffset LastSeen, int OpenTabs)
{
    public DateTimeOffset OnlineSince { get; init; }
}

/// <summary>
/// Remembers when each backoffice user was last seen and which backoffice tabs they have open, so the
/// dashboard can list who is online. Every open backoffice tab sends a heartbeat with its own id, so idle
/// editors are included and each tab is counted once.
/// </summary>
public sealed class BackofficePresence
{
    /// <summary>A tab that misses this many seconds of heartbeats (sent every 60s) is treated as closed.</summary>
    private static readonly TimeSpan TabTimeout = TimeSpan.FromSeconds(150);

    private const int MaxTabsPerUser = 50;

    private readonly ConcurrentDictionary<Guid, UserState> _users = new();
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public BackofficePresence(IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Records any authenticated backoffice request.</summary>
    public void Record(Guid userKey) => GetUser(userKey).LastSeen = _timeProvider.GetUtcNow();

    /// <summary>Records a heartbeat from one backoffice tab.</summary>
    public void RecordTab(Guid userKey, string tabId)
    {
        UserState user = GetUser(userKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        user.LastSeen = now;

        if (user.Tabs.Count < MaxTabsPerUser || user.Tabs.ContainsKey(tabId))
        {
            user.Tabs[tabId] = now;
        }
    }

    /// <summary>Forgets a tab straight away, e.g. when it is closed.</summary>
    public void EndTab(Guid userKey, string tabId)
    {
        if (_users.TryGetValue(userKey, out UserState? user))
        {
            user.Tabs.TryRemove(tabId, out _);
        }
    }

    /// <summary>
    /// Users with an open tab or seen within the backoffice channel's activity window, newest arrivals first.
    /// Ordering by arrival rather than activity keeps rows from jumping between pages on every refresh.
    /// </summary>
    public IReadOnlyList<OnlineBackofficeUser> GetOnline()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset seenCutoff = now - Window;
        DateTimeOffset tabCutoff = now - TabTimeout;

        var online = new List<OnlineBackofficeUser>();
        foreach ((Guid key, UserState user) in _users)
        {
            foreach ((string tabId, DateTimeOffset seen) in user.Tabs)
            {
                if (seen < tabCutoff)
                {
                    user.Tabs.TryRemove(new KeyValuePair<string, DateTimeOffset>(tabId, seen));
                }
            }

            int tabs = user.Tabs.Count;
            if (tabs == 0 && user.LastSeen < seenCutoff)
            {
                _users.TryRemove(new KeyValuePair<Guid, UserState>(key, user));
                continue;
            }

            online.Add(new OnlineBackofficeUser(key, user.LastSeen, tabs) { OnlineSince = user.FirstSeen });
        }

        return online.OrderByDescending(u => u.OnlineSince).ThenBy(u => u.UserKey).ToList();
    }

    private UserState GetUser(Guid userKey) =>
        _users.GetOrAdd(userKey, static (_, now) => new UserState { FirstSeen = now }, _timeProvider.GetUtcNow());

    private TimeSpan Window
    {
        get
        {
            LiveViewOptions options = _options.CurrentValue;
            int? channelWindow = options.Channels.TryGetValue(Constants.Channels.Backoffice, out LiveViewChannelOptions? channel)
                ? channel.ActivityWindowSeconds
                : null;
            return TimeSpan.FromSeconds(channelWindow ?? options.ActivityWindowSeconds);
        }
    }

    private sealed class UserState
    {
        private long _lastSeenTicks;

        public ConcurrentDictionary<string, DateTimeOffset> Tabs { get; } = new();

        public DateTimeOffset FirstSeen { get; init; }

        public DateTimeOffset LastSeen
        {
            get => new(Interlocked.Read(ref _lastSeenTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastSeenTicks, value.UtcTicks);
        }
    }
}
