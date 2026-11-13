using System.Security.Claims;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Presence;

/// <summary>A backoffice user who is online, as one row of the backoffice users table.</summary>
/// <param name="Key">The user's key.</param>
/// <param name="Name">The user's name, or their username when they have no name.</param>
/// <param name="Email">The user's email address, or null when they have none.</param>
/// <param name="AvatarUrls">The user's avatar in Umbraco's sizes, smallest first, or none when they have not set one.</param>
/// <param name="LastSeen">When the user was last seen, in the backoffice or on MCP, whichever is later.</param>
/// <param name="OpenTabs">
/// How many backoffice tabs the user has open, counted from their realtime connections, or from the tabs' heartbeats
/// when realtime is unavailable. Zero when they are only on MCP.
/// </param>
/// <param name="IsConnected">
/// Whether the user has a realtime connection from an open backoffice tab, so a message reaches them straight away.
/// </param>
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
    internal async Task SeenAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated == true && user.Identity.GetUserKey() is { } key)
        {
            await _store.SeenAsync(ActivitySets.BackofficeUsers, ActivitySets.Person(key), Now);
        }
    }

    /// <summary>Records a heartbeat from one of the user's backoffice tabs.</summary>
    internal async Task TabHeartbeatAsync(Guid userKey, string tabId)
    {
        DateTimeOffset now = Now;
        await _store.SeenAsync(ActivitySets.BackofficeUsers, ActivitySets.Person(userKey), now);

        string tabs = ActivitySets.BackofficeTabs(userKey);
        IReadOnlyList<LiveViewActivity> open = await _store.GetActiveAsync(tabs, now - TabTimeout);
        if (open.Count < MaxTabsPerUser || open.Any(t => t.Key == tabId))
        {
            await _store.SeenAsync(tabs, tabId, now);
        }
    }

    /// <summary>Forgets a tab straight away, e.g. when it is closed.</summary>
    internal Task TabClosedAsync(Guid userKey, string tabId) => _store.RemoveAsync(ActivitySets.BackofficeTabs(userKey), tabId);

    /// <summary>Records a finished MCP request by the user.</summary>
    internal Task McpSeenAsync(Guid userKey) => _mcp.SeenAsync(userKey);

    /// <summary>Records an open MCP request by the user, who stays online on MCP until it is disposed.</summary>
    internal Task<IAsyncDisposable> McpOpenedAsync(Guid userKey) => _mcp.OpenedAsync(userKey);

    /// <summary>Forgets a user whose account has gone or been disabled.</summary>
    internal async Task ForgetAsync(Guid userKey)
    {
        string tabs = ActivitySets.BackofficeTabs(userKey);
        foreach (LiveViewActivity tab in await _store.GetActiveAsync(tabs, DateTimeOffset.MinValue))
        {
            await _store.RemoveAsync(tabs, tab.Key);
        }

        await _store.RemoveAsync(ActivitySets.BackofficeUsers, ActivitySets.Person(userKey));
        await _mcp.ForgetAsync(userKey);
    }

    /// <summary>How many users are online, in the backoffice, on MCP or both.</summary>
    internal async Task<int> CountOnlineAsync() => (await PeopleAsync()).Count;

    /// <summary>Every online user's key, in the backoffice or on MCP, read once.</summary>
    internal async Task<IReadOnlyList<Guid>> AllOnlineAsync() => (await PeopleAsync()).Select(u => u.Key).ToList();

    /// <summary>Whether any user online is using MCP, so the table shows how each one is online.</summary>
    internal async Task<bool> AnyOnMcpAsync() => (await _mcp.SnapshotAsync()).Count > 0;

    /// <summary>
    /// Every user online in the backoffice, read once, e.g. to address a message to all of them. Users only on MCP
    /// are left out, since a backoffice message is shown in a backoffice tab.
    /// </summary>
    internal async Task<IReadOnlyList<Guid>> AllInBackofficeAsync() => (await SnapshotAsync()).Select(u => u.Key).ToList();

    /// <summary>
    /// One page of online users, newest arrivals first, and how many are online in total. Users whose account no
    /// longer exists or is disabled are forgotten before anything is counted.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<OnlineBackofficeUser> Page)> PageAsync(int skip, int take)
    {
        while (true)
        {
            Dictionary<Guid, Row> inBackoffice = (await SnapshotAsync()).ToDictionary(u => u.Key);
            List<OnlinePerson> online = await PeopleAsync(inBackoffice);
            List<OnlinePerson> page = online.Skip(skip).Take(take).ToList();
            Dictionary<Guid, BackofficeUserDetails> found = (await _directory.GetAsync(page.Select(u => u.Key).ToList()))
                .ToDictionary(u => u.Key);

            List<Guid> gone = page.Where(u => !found.ContainsKey(u.Key)).Select(u => u.Key).ToList();
            if (gone.Count > 0)
            {
                // Read again without them, so neither the page nor the total includes a deleted or disabled user.
                foreach (Guid key in gone)
                {
                    await ForgetAsync(key);
                }

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
    private async Task<List<OnlinePerson>> PeopleAsync(IReadOnlyDictionary<Guid, Row>? inBackoffice = null) =>
        OnlinePerson.Merge(
            (inBackoffice ?? (await SnapshotAsync()).ToDictionary(u => u.Key)).Select(u => (u.Key, u.Value.First, u.Value.Last)),
            await _mcp.SnapshotAsync());

    /// <summary>
    /// Online users in arrival order, newest first: those with an open tab, or seen within the window. Tabs past their
    /// timeout are not counted. The users are read once and every user's tabs are counted in one call.
    /// </summary>
    private async Task<List<Row>> SnapshotAsync()
    {
        DateTimeOffset now = Now;
        DateTimeOffset seenCutoff = now - Window;
        DateTimeOffset tabCutoff = now - TabTimeout;
        IReadOnlyList<LiveViewActivity> users = await _store.GetActiveAsync(
            ActivitySets.BackofficeUsers, now - (Window > TabTimeout ? Window : TabTimeout));
        var keys = users.Select(u => Guid.ParseExact(u.Key, "N")).ToList();
        IReadOnlyList<int> tabCounts = keys.Count == 0
            ? []
            : await _store.CountActiveAsync(keys.Select(ActivitySets.BackofficeTabs).ToList(), tabCutoff);

        var online = new List<Row>();
        for (int i = 0; i < users.Count; i++)
        {
            LiveViewActivity user = users[i];
            if (tabCounts[i] > 0 || user.Last >= seenCutoff)
            {
                online.Add(new Row(keys[i], user.First.UtcTicks, user.Last.UtcTicks, tabCounts[i]));
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
