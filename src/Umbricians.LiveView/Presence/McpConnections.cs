using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// One person online, one row of a table: when they came online, and when they were last seen each way they are
/// online, on the site (the website or the backoffice) and on MCP; null for a way they are not.
/// </summary>
internal readonly record struct OnlinePerson(Guid Key, long First, long? SiteLast, long? McpLast)
{
    public long Last => Math.Max(SiteLast ?? 0, McpLast ?? 0);

    /// <summary>
    /// Puts together the people online on the site and on MCP, newest arrivals first. Someone online both ways
    /// arrived when the first of them began.
    /// </summary>
    public static List<OnlinePerson> Merge(
        IEnumerable<(Guid Key, long First, long Last)> site, IEnumerable<(Guid Key, long First, long Last)> mcp)
    {
        var people = new Dictionary<Guid, OnlinePerson>();
        foreach ((Guid key, long first, long last) in site)
        {
            people[key] = new OnlinePerson(key, first, last, null);
        }

        foreach ((Guid key, long first, long last) in mcp)
        {
            people[key] = people.TryGetValue(key, out OnlinePerson onSite)
                ? onSite with { First = Math.Min(onSite.First, first), McpLast = last }
                : new OnlinePerson(key, first, null, last);
        }

        List<OnlinePerson> online = people.Values.ToList();
        online.Sort(static (a, b) => b.First != a.First ? b.First.CompareTo(a.First) : a.Key.CompareTo(b.Key));
        return online;
    }
}

/// <summary>
/// The MCP side of a presence module: which of its people are using MCP. A caller stays online while any of their
/// MCP requests is open, so long-lived streams count while connected, and for the MCP channel's activity window after.
/// </summary>
internal sealed class McpConnections
{
    private readonly ILiveViewActivityStore _store;
    private readonly string _set;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public McpConnections(
        ILiveViewActivityStore store, string set, IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _store = store;
        _set = set;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Records one MCP request that has finished.</summary>
    public Task SeenAsync(Guid key) => _store.SeenAsync(_set, ActivitySets.Person(key), Now);

    /// <summary>Records an MCP request that is still open; the caller stays online until it is disposed.</summary>
    public async Task<IAsyncDisposable> OpenedAsync(Guid key)
    {
        await _store.OpenedAsync(_set, ActivitySets.Person(key), Now);
        return new Hold(this, key);
    }

    public Task ForgetAsync(Guid key) => _store.RemoveAsync(_set, ActivitySets.Person(key));

    /// <summary>Callers online on MCP, last seen now while a request of theirs is open.</summary>
    public async Task<IReadOnlyList<(Guid Key, long First, long Last)>> SnapshotAsync()
    {
        DateTimeOffset now = Now;
        return (await _store.GetActiveAsync(_set, now - Window))
            .Select(a => (Guid.ParseExact(a.Key, "N"), a.First.UtcTicks, a.Open > 0 ? now.UtcTicks : a.Last.UtcTicks))
            .ToList();
    }

    private Task ClosedAsync(Guid key) => _store.ClosedAsync(_set, ActivitySets.Person(key), Now);

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    private TimeSpan Window => ChannelSettings.Of(_options.CurrentValue, Constants.Channels.Mcp).ActivityWindow;

    private sealed class Hold(McpConnections connections, Guid key) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await connections.ClosedAsync(key);
            }
        }
    }
}
