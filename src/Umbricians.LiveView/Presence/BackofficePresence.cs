using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

internal sealed record OnlineBackofficeUser(
    Guid Key, string Name, string? Email, DateTimeOffset LastSeen, int OpenTabs, bool IsConnected);

/// <summary>
/// Knows which backoffice users are online: those with a backoffice tab open, or seen within the backoffice
/// channel's activity window, whose account still exists and is not disabled. It is the only source for both the
/// backoffice users line on the chart and the backoffice users table, so the two always agree.
/// </summary>
/// <remarks>
/// Users are always held by their key. Every open backoffice tab sends a heartbeat with its own id, so idle editors
/// are included and each tab is counted once. Online users are listed in arrival order, newest first, so rows don't
/// jump between pages as editors keep working.
/// </remarks>
public sealed class BackofficePresence
{
    /// <summary>A tab that misses this many seconds of heartbeats (sent every 60s) is treated as closed.</summary>
    internal static readonly TimeSpan TabTimeout = TimeSpan.FromSeconds(150);

    internal const int MaxTabsPerUser = 50;

    private static readonly ImmutableDictionary<string, long> NoTabs = ImmutableDictionary<string, long>.Empty;

    private readonly ConcurrentDictionary<Guid, Visit> _users = new();
    private readonly IBackofficeUserDirectory _directory;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    internal BackofficePresence(
        IBackofficeUserDirectory directory, IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _directory = directory;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Records that the signed-in backoffice user made a request. Anyone else is ignored.</summary>
    internal void Seen(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated == true && user.Identity.GetUserKey() is { } key)
        {
            _users.AddOrUpdate(key, static (_, n) => new Visit(n, n, NoTabs), static (_, seen, n) => seen with { Last = n }, Now);
        }
    }

    /// <summary>Records a heartbeat from one of the user's backoffice tabs.</summary>
    internal void TabHeartbeat(Guid userKey, string tabId) =>
        _users.AddOrUpdate(
            userKey,
            static (_, s) => new Visit(s.Now, s.Now, NoTabs.SetItem(s.TabId, s.Now)),
            static (_, seen, s) => seen with
            {
                Last = s.Now,
                Tabs = seen.Tabs.Count < MaxTabsPerUser || seen.Tabs.ContainsKey(s.TabId)
                    ? seen.Tabs.SetItem(s.TabId, s.Now)
                    : seen.Tabs,
            },
            (TabId: tabId, Now: Now));

    /// <summary>Forgets a tab straight away, e.g. when it is closed.</summary>
    internal void TabClosed(Guid userKey, string tabId)
    {
        while (_users.TryGetValue(userKey, out Visit? seen) && seen.Tabs.ContainsKey(tabId))
        {
            if (_users.TryUpdate(userKey, seen with { Tabs = seen.Tabs.Remove(tabId) }, seen))
            {
                return;
            }
        }
    }

    /// <summary>Forgets a user whose account has gone or been disabled.</summary>
    internal void Forget(Guid userKey) => _users.TryRemove(userKey, out _);

    internal int CountOnline() => Snapshot().Count;

    /// <summary>Every online user's key, read once, e.g. to address a message to all of them.</summary>
    internal IReadOnlyList<Guid> AllOnline() => Snapshot().Select(u => u.Key).ToList();

    /// <summary>
    /// One page of online users, newest arrivals first, and how many are online in total. Users whose account no
    /// longer exists or is disabled are forgotten before anything is counted.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<OnlineBackofficeUser> Page)> PageAsync(int skip, int take)
    {
        while (true)
        {
            List<(Guid Key, Visit Visit)> online = Snapshot();
            List<(Guid Key, Visit Visit)> page = online.Skip(skip).Take(take).ToList();
            Dictionary<Guid, BackofficeUserDetails> found = (await _directory.GetAsync(page.Select(u => u.Key).ToList()))
                .ToDictionary(u => u.Key);

            List<Guid> gone = page.Where(u => !found.ContainsKey(u.Key)).Select(u => u.Key).ToList();
            if (gone.Count > 0)
            {
                // Read again without them, so neither the page nor the total includes a deleted or disabled user.
                gone.ForEach(Forget);
                continue;
            }

            IReadOnlyList<OnlineBackofficeUser> rows = page
                .Select(u =>
                {
                    BackofficeUserDetails details = found[u.Key];

                    // Every open backoffice tab holds one realtime connection, which closes the moment the tab does,
                    // so that is the exact tab count. Heartbeats only stand in when realtime is unavailable.
                    return new OnlineBackofficeUser(
                        u.Key,
                        details.Name,
                        details.Email,
                        new DateTimeOffset(u.Visit.Last, TimeSpan.Zero),
                        details.Connections > 0 ? details.Connections : u.Visit.Tabs.Count,
                        details.Connections > 0);
                })
                .ToList();

            return (online.Count, rows);
        }
    }

    /// <summary>
    /// Online users in arrival order, newest first. Tabs past their timeout, and users with no open tab who are past
    /// the window, are dropped as it goes.
    /// </summary>
    private List<(Guid Key, Visit Visit)> Snapshot()
    {
        long now = Now;
        long seenCutoff = now - Window.Ticks;
        long tabCutoff = now - TabTimeout.Ticks;
        var online = new List<(Guid Key, Visit Visit)>(_users.Count);
        foreach ((Guid key, Visit seen) in _users)
        {
            Visit current = seen;
            List<string> closed = seen.Tabs.Where(t => t.Value < tabCutoff).Select(t => t.Key).ToList();
            if (closed.Count > 0)
            {
                current = seen with { Tabs = seen.Tabs.RemoveRange(closed) };
                if (!_users.TryUpdate(key, current, seen))
                {
                    // Updated meanwhile, so it was just seen; count it as it was read.
                    current = seen;
                }
            }

            if (current.Tabs.IsEmpty && current.Last < seenCutoff)
            {
                _users.TryRemove(new KeyValuePair<Guid, Visit>(key, current));
            }
            else
            {
                online.Add((key, current));
            }
        }

        online.Sort(static (a, b) => b.Visit.First != a.Visit.First ? b.Visit.First.CompareTo(a.Visit.First) : a.Key.CompareTo(b.Key));
        return online;
    }

    private long Now => _timeProvider.GetUtcNow().UtcTicks;

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

    /// <summary>When the user came online, when they were last seen, and when each open tab last sent a heartbeat.</summary>
    private sealed record Visit(long First, long Last, ImmutableDictionary<string, long> Tabs);
}
