using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;

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
    private readonly ConcurrentDictionary<Guid, Visit> _callers = new();
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public McpConnections(IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Records one MCP request that has finished.</summary>
    public void Seen(Guid key) =>
        _callers.AddOrUpdate(key, static (_, n) => new Visit(n, n, 0), static (_, seen, n) => seen with { Last = n }, Now);

    /// <summary>Records an MCP request that is still open; the caller stays online until it is disposed.</summary>
    public IDisposable Opened(Guid key)
    {
        _callers.AddOrUpdate(
            key, static (_, n) => new Visit(n, n, 1), static (_, seen, n) => seen with { Last = n, Open = seen.Open + 1 }, Now);
        return new Hold(this, key);
    }

    public void Forget(Guid key) => _callers.TryRemove(key, out _);

    /// <summary>
    /// Callers online on MCP, last seen now while a request of theirs is open. Those with nothing open who are past
    /// the window are dropped as it goes.
    /// </summary>
    public IEnumerable<(Guid Key, long First, long Last)> Snapshot()
    {
        long now = Now;
        long cutoff = now - Window.Ticks;
        foreach ((Guid key, Visit seen) in _callers)
        {
            if (seen.Open == 0 && seen.Last < cutoff)
            {
                _callers.TryRemove(new KeyValuePair<Guid, Visit>(key, seen));
            }
            else
            {
                yield return (key, seen.First, seen.Open > 0 ? now : seen.Last);
            }
        }
    }

    private void Closed(Guid key) =>
        _callers.AddOrUpdate(
            key,
            static (_, n) => new Visit(n, n, 0),
            static (_, seen, n) => seen with { Last = n, Open = Math.Max(0, seen.Open - 1) },
            Now);

    private long Now => _timeProvider.GetUtcNow().UtcTicks;

    private TimeSpan Window
    {
        get
        {
            LiveViewOptions options = _options.CurrentValue;
            int? channelWindow = options.Channels.TryGetValue(Constants.Channels.Mcp, out LiveViewChannelOptions? channel)
                ? channel.ActivityWindowSeconds
                : null;
            return TimeSpan.FromSeconds(channelWindow ?? options.ActivityWindowSeconds);
        }
    }

    /// <summary>When the caller first came online on MCP, when last seen there, and how many MCP requests are open.</summary>
    private sealed record Visit(long First, long Last, int Open);

    private sealed class Hold(McpConnections connections, Guid key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                connections.Closed(key);
            }
        }
    }
}
