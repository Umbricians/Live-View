namespace Umbricians.LiveView.Storage;

/// <summary>
/// Where Live View keeps who is active: sessions per channel, and the backoffice users, their tabs, members and MCP
/// callers who are online. In memory by default, so each server has its own; replace it to share them between
/// load-balanced servers.
/// </summary>
/// <remarks>
/// Entries live in named sets. Set names and keys are opaque strings (session keys are already hashed). The store only
/// holds data: the activity windows and who counts as online are decided by Live View, which passes the cutoff time.
/// It is called on every tracked request, so keep it quick. An open count left by a server that stops is never
/// closed, so a shared store should expire entries.
/// </remarks>
public interface ILiveViewActivityStore
{
    /// <summary>Records activity now: adds the entry (First and Last are <paramref name="at"/>, Open is 0) or sets its Last.</summary>
    void Seen(string set, string key, DateTimeOffset at);

    /// <summary>Records a request that is still open: as <see cref="Seen"/>, and Open + 1. An entry is active while Open is above 0.</summary>
    void Opened(string set, string key, DateTimeOffset at);

    /// <summary>Records an open request finishing: Last is <paramref name="at"/> and Open - 1, not below 0. Does nothing if the entry has gone.</summary>
    void Closed(string set, string key, DateTimeOffset at);

    /// <summary>Removes the entry straight away. Does nothing if it has gone.</summary>
    void Remove(string set, string key);

    /// <summary>
    /// The entries with Open above 0 or Last at or after <paramref name="since"/>. Entries that are neither may be
    /// discarded; callers always pass the same window for a set.
    /// </summary>
    IReadOnlyList<LiveViewActivity> GetActive(string set, DateTimeOffset since);

    /// <summary>How many entries <see cref="GetActive"/> would return.</summary>
    int CountActive(string set, DateTimeOffset since);
}

/// <summary>One entry of an <see cref="ILiveViewActivityStore"/>: when it first appeared, when it was last seen, and how many requests are open.</summary>
public sealed record LiveViewActivity(string Key, DateTimeOffset First, DateTimeOffset Last, int Open);
