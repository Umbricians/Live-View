using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Presence;

internal sealed record OnlineMember(Guid Key, string Name, string? Email, string? MemberType, DateTimeOffset LastSeen);

/// <summary>
/// Knows which members are online: those seen on the website within the members channel's activity window, who
/// have not signed out since and whose account still exists. It is the only source for both the members line on the
/// chart and the members table, so the two always agree.
/// </summary>
/// <remarks>
/// Members are always held by their key. Online members are listed in arrival order, newest first, so rows don't
/// jump between pages as members keep browsing.
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
    private readonly IMemberDirectory _directory;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    internal MemberPresence(IMemberDirectory directory, IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _directory = directory;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>Records that the signed-in member made a front-end request. Anyone else is ignored.</summary>
    internal void Seen(ClaimsPrincipal user)
    {
        if (ResolveKey(user) is not { } key)
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

    /// <summary>Takes a member offline straight away, and keeps them off while their sign-out request completes.</summary>
    internal void SignedOut(Guid memberKey)
    {
        _signedOutUntil[memberKey] = Now + SignedOutMarkerLifetime.Ticks;
        _members.TryRemove(memberKey, out _);
    }

    /// <summary>Forgets a member whose account has gone.</summary>
    internal void Forget(Guid memberKey) => _members.TryRemove(memberKey, out _);

    internal int CountOnline() => Snapshot().Count;

    /// <summary>Every online member's key, read once, e.g. to address a message to all of them.</summary>
    internal IReadOnlyList<Guid> AllOnline() => Snapshot().Select(m => m.Key).ToList();

    /// <summary>
    /// One page of online members, newest arrivals first, and how many are online in total. Members whose account
    /// no longer exists are forgotten before anything is counted.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<OnlineMember> Page)> PageAsync(int skip, int take)
    {
        while (true)
        {
            List<(Guid Key, Visit Visit)> online = Snapshot();
            List<(Guid Key, Visit Visit)> page = online.Skip(skip).Take(take).ToList();
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
                        m.Key, details.Name, details.Email, details.MemberType, new DateTimeOffset(m.Visit.Last, TimeSpan.Zero));
                })
                .ToList();

            return (online.Count, rows);
        }
    }

    /// <summary>Online members in arrival order, newest first. Members past the window are dropped as it goes.</summary>
    private List<(Guid Key, Visit Visit)> Snapshot()
    {
        long cutoff = Now - Window.Ticks;
        var online = new List<(Guid Key, Visit Visit)>(_members.Count);
        foreach ((Guid key, Visit seen) in _members)
        {
            if (seen.Last < cutoff)
            {
                _members.TryRemove(new KeyValuePair<Guid, Visit>(key, seen));
            }
            else
            {
                online.Add((key, seen));
            }
        }

        foreach ((Guid key, long until) in _signedOutUntil)
        {
            if (until <= Now)
            {
                _signedOutUntil.TryRemove(new KeyValuePair<Guid, long>(key, until));
            }
        }

        online.Sort(static (a, b) => b.Visit.First != a.Visit.First ? b.Visit.First.CompareTo(a.Visit.First) : a.Key.CompareTo(b.Key));
        return online;
    }

    /// <summary>
    /// Members carry their key in the "sub" claim when it is there; otherwise the name identifier holds their key
    /// or their numeric id, which is turned into the key.
    /// </summary>
    private Guid? ResolveKey(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (user.Identity.GetUserKey() is { } key)
        {
            return key;
        }

        string? id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(id, out Guid parsed))
        {
            return parsed;
        }

        return int.TryParse(id, out int memberId) ? _directory.FindKey(memberId) : null;
    }

    private long Now => _timeProvider.GetUtcNow().UtcTicks;

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
