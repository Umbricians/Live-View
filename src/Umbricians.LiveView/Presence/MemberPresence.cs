using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

/// <param name="WebsiteLastSeen">When the member was last seen on the website, or null when they are not on it.</param>
/// <param name="McpLastSeen">When the member last used MCP, or null when they are not on it.</param>
internal sealed record OnlineMember(
    Guid Key,
    string Name,
    string? Email,
    string? MemberType,
    string? AvatarUrl,
    DateTimeOffset LastSeen,
    DateTimeOffset? WebsiteLastSeen,
    DateTimeOffset? McpLastSeen);

/// <summary>
/// Knows which members are online: those seen on the website within the members channel's activity window who have
/// not signed out since, and those using MCP, whose account still exists. It is the only source for both the members
/// line on the chart and the members table, so the two always agree.
/// </summary>
/// <remarks>
/// Members are always held by their key, with one row each however they are online (the website, MCP, or both).
/// Rows are listed in arrival order, newest first, so they don't jump between pages as members keep browsing.
/// </remarks>
public sealed class MemberPresence
{
    /// <summary>
    /// How long "seen" is ignored after a member signs out. The sign-out request itself is still made as the member,
    /// and would otherwise put them straight back online as it completes.
    /// </summary>
    internal static readonly TimeSpan SignedOutMarkerLifetime = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, Visit> _members = new();
    private readonly ConcurrentDictionary<Guid, long> _signedOutUntil = new();
    private readonly McpConnections _mcp;
    private readonly IMemberDirectory _directory;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    internal MemberPresence(IMemberDirectory directory, IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _directory = directory;
        _options = options;
        _timeProvider = timeProvider;
        _mcp = new McpConnections(options, timeProvider);
    }

    /// <summary>Records that the signed-in member made a front-end request. Anyone else is ignored.</summary>
    internal void Seen(ClaimsPrincipal user)
    {
        if (PrincipalKey.Resolve(user, _directory.FindKey) is not { } key)
        {
            return;
        }

        long now = Now;
        if (_signedOutUntil.TryGetValue(key, out long until))
        {
            if (now < until)
            {
                return;
            }

            _signedOutUntil.TryRemove(new KeyValuePair<Guid, long>(key, until));
        }

        _members.AddOrUpdate(key, static (_, n) => new Visit(n, n), static (_, seen, n) => seen with { Last = n }, now);
    }

    /// <summary>Records a finished MCP request by the member.</summary>
    internal void McpSeen(Guid memberKey) => _mcp.Seen(memberKey);

    /// <summary>Records an open MCP request by the member, who stays online on MCP until it is disposed.</summary>
    internal IDisposable McpOpened(Guid memberKey) => _mcp.Opened(memberKey);

    /// <summary>
    /// Takes a member off the website straight away, and keeps them off while their sign-out request completes. Their
    /// MCP connection, signed in separately, is left alone.
    /// </summary>
    internal void SignedOut(Guid memberKey)
    {
        _signedOutUntil[memberKey] = Now + SignedOutMarkerLifetime.Ticks;
        _members.TryRemove(memberKey, out _);
    }

    /// <summary>Forgets a member whose account has gone.</summary>
    internal void Forget(Guid memberKey)
    {
        _members.TryRemove(memberKey, out _);
        _mcp.Forget(memberKey);
    }

    /// <summary>How many members are online, on the website, MCP or both.</summary>
    internal int CountOnline() => People().Count;

    /// <summary>Every online member's key, read once, e.g. to address a message to all of them.</summary>
    internal IReadOnlyList<Guid> AllOnline() => People().Select(m => m.Key).ToList();

    /// <summary>Whether any member online is using MCP, so the table shows how each one is online.</summary>
    internal bool AnyOnMcp() => _mcp.Snapshot().Any();

    /// <summary>
    /// One page of online members, newest arrivals first, and how many are online in total. Members whose account
    /// no longer exists are forgotten before anything is counted.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<OnlineMember> Page)> PageAsync(int skip, int take)
    {
        while (true)
        {
            List<OnlinePerson> online = People();
            List<OnlinePerson> page = online.Skip(skip).Take(take).ToList();
            Dictionary<Guid, MemberDetails> found = (await _directory.GetAsync(page.Select(m => m.Key).ToList()))
                .ToDictionary(m => m.Key);

            List<Guid> gone = page.Where(m => !found.ContainsKey(m.Key)).Select(m => m.Key).ToList();
            if (gone.Count > 0)
            {
                // Read again without them, so neither the page nor the total includes a deleted member.
                gone.ForEach(Forget);
                continue;
            }

            IReadOnlyList<OnlineMember> rows = page
                .Select(m =>
                {
                    MemberDetails details = found[m.Key];
                    return new OnlineMember(
                        m.Key,
                        details.Name,
                        details.Email,
                        details.MemberType,
                        details.AvatarUrl,
                        At(m.Last),
                        m.SiteLast is { } website ? At(website) : null,
                        m.McpLast is { } mcp ? At(mcp) : null);
                })
                .ToList();

            return (online.Count, rows);
        }
    }

    /// <summary>Every online member, on the website, MCP or both, in arrival order.</summary>
    private List<OnlinePerson> People() => OnlinePerson.Merge(WebsiteSnapshot(), _mcp.Snapshot());

    /// <summary>Members online on the website. Members past the window are dropped as it goes.</summary>
    private List<(Guid Key, long First, long Last)> WebsiteSnapshot()
    {
        long cutoff = Now - Window.Ticks;
        var online = new List<(Guid Key, long First, long Last)>(_members.Count);
        foreach ((Guid key, Visit seen) in _members)
        {
            if (seen.Last < cutoff)
            {
                _members.TryRemove(new KeyValuePair<Guid, Visit>(key, seen));
            }
            else
            {
                online.Add((key, seen.First, seen.Last));
            }
        }

        foreach ((Guid key, long until) in _signedOutUntil)
        {
            if (until <= Now)
            {
                _signedOutUntil.TryRemove(new KeyValuePair<Guid, long>(key, until));
            }
        }

        return online;
    }

    private long Now => _timeProvider.GetUtcNow().UtcTicks;

    private static DateTimeOffset At(long ticks) => new(ticks, TimeSpan.Zero);

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

    private sealed record Visit(long First, long Last);
}
