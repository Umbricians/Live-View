using System.Security.Claims;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Presence;

/// <param name="BackofficeLastSeen">When the user was last seen in the backoffice, or null when they are only on MCP.</param>
/// <param name="McpLastSeen">When the user last used MCP, or null when they are not on it.</param>
internal sealed record OnlineBackofficeUser(
    Guid Key,
    string Name,
    string? Email,
    IReadOnlyList<string> AvatarUrls,
    DateTimeOffset LastSeen,
    int OpenTabs,
    bool IsConnected,
    DateTimeOffset? BackofficeLastSeen,
    DateTimeOffset? McpLastSeen);

/// <summary>
/// Knows which backoffice users are online: those with a backoffice tab open or seen within the backoffice channel's
/// activity window, and those using MCP, whose account still exists and is not disabled. It is the only source for
/// both the backoffice users line on the chart and the backoffice users table, so the two always agree.
/// </summary>
/// <remarks>
/// Users are always held by their key. Every open backoffice tab sends a heartbeat with its own id, so idle editors
/// are included and each tab is counted once. Each user has one row however they are online (the backoffice, MCP,
/// or both). Rows are listed in arrival order, newest first, so they don't jump between pages as editors keep working.
/// </remarks>
internal sealed class BackofficePresence
{
    /// <summary>A tab that misses this many seconds of heartbeats (sent every 60s) is treated as closed.</summary>
    internal static readonly TimeSpan TabTimeout = TimeSpan.FromSeconds(150);

    internal const int MaxTabsPerUser = 50;

    private readonly ILiveViewActivityStore _store;
    private readonly McpConnections _mcp;
    private readonly IBackofficeUserDirectory _directory;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    internal BackofficePresence(
        ILiveViewActivityStore store,
        IBackofficeUserDirectory directory,
        IOptionsMonitor<LiveViewOptions> options,
        TimeProvider timeProvider)
    {
        _store = store;
        _directory = directory;
        _options = options;
        _timeProvider = timeProvider;
        _mcp = new McpConnections(store, ActivitySets.BackofficeMcp, options, timeProvider);
    }

    /// <summary>Records that the signed-in backoffice user made a request. Anyone else is ignored.</summary>
    internal void Seen(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated == true && user.Identity.GetUserKey() is { } key)
        {
            _store.Seen(ActivitySets.BackofficeUsers, ActivitySets.Person(key), Now);
        }
    }

    /// <summary>Records a heartbeat from one of the user's backoffice tabs.</summary>
    internal void TabHeartbeat(Guid userKey, string tabId)
    {
        DateTimeOffset now = Now;
        _store.Seen(ActivitySets.BackofficeUsers, ActivitySets.Person(userKey), now);

        string tabs = ActivitySets.BackofficeTabs(userKey);
        IReadOnlyList<LiveViewActivity> open = _store.GetActive(tabs, now - TabTimeout);
        if (open.Count < MaxTabsPerUser || open.Any(t => t.Key == tabId))
        {
            _store.Seen(tabs, tabId, now);
        }
    }

    /// <summary>Forgets a tab straight away, e.g. when it is closed.</summary>
    internal void TabClosed(Guid userKey, string tabId) => _store.Remove(ActivitySets.BackofficeTabs(userKey), tabId);

    /// <summary>Records a finished MCP request by the user.</summary>
    internal void McpSeen(Guid userKey) => _mcp.Seen(userKey);

    /// <summary>Records an open MCP request by the user, who stays online on MCP until it is disposed.</summary>
    internal IDisposable McpOpened(Guid userKey) => _mcp.Opened(userKey);

    /// <summary>Forgets a user whose account has gone or been disabled.</summary>
    internal void Forget(Guid userKey)
    {
        string tabs = ActivitySets.BackofficeTabs(userKey);
        foreach (LiveViewActivity tab in _store.GetActive(tabs, DateTimeOffset.MinValue))
        {
            _store.Remove(tabs, tab.Key);
        }

        _store.Remove(ActivitySets.BackofficeUsers, ActivitySets.Person(userKey));
        _mcp.Forget(userKey);
    }

    /// <summary>How many users are online, in the backoffice, on MCP or both.</summary>
    internal int CountOnline() => People().Count;

    /// <summary>Every online user's key, in the backoffice or on MCP, read once.</summary>
    internal IReadOnlyList<Guid> AllOnline() => People().Select(u => u.Key).ToList();

    /// <summary>Whether any user online is using MCP, so the table shows how each one is online.</summary>
    internal bool AnyOnMcp() => _mcp.Snapshot().Any();

    /// <summary>
    /// Every user online in the backoffice, read once, e.g. to address a message to all of them. Users only on MCP
    /// are left out, since a backoffice message is shown in a backoffice tab.
    /// </summary>
    internal IReadOnlyList<Guid> AllInBackoffice() => Snapshot().Select(u => u.Key).ToList();

    /// <summary>
    /// One page of online users, newest arrivals first, and how many are online in total. Users whose account no
    /// longer exists or is disabled are forgotten before anything is counted.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<OnlineBackofficeUser> Page)> PageAsync(int skip, int take)
    {
        while (true)
        {
            Dictionary<Guid, Row> inBackoffice = Snapshot().ToDictionary(u => u.Key);
            List<OnlinePerson> online = People(inBackoffice);
            List<OnlinePerson> page = online.Skip(skip).Take(take).ToList();
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
                    bool backoffice = inBackoffice.TryGetValue(u.Key, out Row visit);

                    // Every open backoffice tab holds one realtime connection, which closes the moment the tab does,
                    // so that is the exact tab count. Heartbeats only stand in when realtime is unavailable.
                    return new OnlineBackofficeUser(
                        u.Key,
                        details.Name,
                        details.Email,
                        details.AvatarUrls,
                        At(u.Last),
                        !backoffice ? 0 : details.Connections > 0 ? details.Connections : visit.Tabs,
                        backoffice && details.Connections > 0,
                        u.SiteLast is { } inTheBackoffice ? At(inTheBackoffice) : null,
                        u.McpLast is { } mcp ? At(mcp) : null);
                })
                .ToList();

            return (online.Count, rows);
        }
    }

    /// <summary>Every online user, in the backoffice, on MCP or both, in arrival order.</summary>
    private List<OnlinePerson> People(IReadOnlyDictionary<Guid, Row>? inBackoffice = null) =>
        OnlinePerson.Merge(
            (inBackoffice ?? Snapshot().ToDictionary(u => u.Key)).Select(u => (u.Key, u.Value.First, u.Value.Last)),
            _mcp.Snapshot());

    /// <summary>
    /// Online users in arrival order, newest first: those with an open tab, or seen within the window. Tabs past their
    /// timeout are not counted.
    /// </summary>
    private List<Row> Snapshot()
    {
        DateTimeOffset now = Now;
        DateTimeOffset seenCutoff = now - Window;
        DateTimeOffset tabCutoff = now - TabTimeout;
        var online = new List<Row>();
        foreach (LiveViewActivity user in _store.GetActive(ActivitySets.BackofficeUsers, now - (Window > TabTimeout ? Window : TabTimeout)))
        {
            Guid key = Guid.ParseExact(user.Key, "N");
            int tabs = _store.CountActive(ActivitySets.BackofficeTabs(key), tabCutoff);
            if (tabs > 0 || user.Last >= seenCutoff)
            {
                online.Add(new Row(key, user.First.UtcTicks, user.Last.UtcTicks, tabs));
            }
        }

        online.Sort(static (a, b) => b.First != a.First ? b.First.CompareTo(a.First) : a.Key.CompareTo(b.Key));
        return online;
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    private static DateTimeOffset At(long ticks) => new(ticks, TimeSpan.Zero);

    private TimeSpan Window => ChannelSettings.Of(_options.CurrentValue, Constants.Channels.Backoffice).ActivityWindow;

    /// <summary>A user in the backoffice: when they came online, when they were last seen, and how many tabs are open.</summary>
    private readonly record struct Row(Guid Key, long First, long Last, int Tabs);
}
