using Microsoft.Extensions.Options;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests;

internal sealed class InMemoryBackofficeUserDirectory : IBackofficeUserDirectory
{
    private readonly Dictionary<Guid, BackofficeUserDetails> _users = new();

    /// <summary>How many times the directory has been asked to look users up.</summary>
    public int Lookups { get; private set; }

    /// <summary>Adds a user with a key of your choosing, e.g. one a member has too.</summary>
    public void Add(Guid key, string name) => _users[key] = new BackofficeUserDetails(key, name, null, 0, []);

    public Guid Add(string name, string? email = null, IReadOnlyList<string>? avatarUrls = null)
    {
        var key = Guid.NewGuid();
        _users[key] = new BackofficeUserDetails(key, name, email, 0, avatarUrls ?? []);
        return key;
    }

    /// <summary>Stands in for a user who is deleted or disabled: either way they are no longer found.</summary>
    public void Remove(Guid key) => _users.Remove(key);

    public void SetConnections(Guid key, int connections) => _users[key] = _users[key] with { Connections = connections };

    public Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<BackofficeUserDetails>>(_users.Values.Where(u => keys.Contains(u.Key)).ToList());
    }
}

internal sealed class InMemoryMemberDirectory : IMemberDirectory
{
    private readonly Dictionary<int, MemberDetails> _members = new();

    public Guid Add(int id, string name, string? email = null, string? memberType = null, string? avatarUrl = null)
    {
        var key = Guid.NewGuid();
        _members[id] = new MemberDetails(key, name, email, memberType, avatarUrl);
        return key;
    }

    /// <summary>Adds a member with a key of your choosing, e.g. one a backoffice user has too.</summary>
    public void Add(int id, Guid key, string name) => _members[id] = new MemberDetails(key, name, null, null, null);

    public void Remove(Guid key) => _members.Remove(_members.Single(m => m.Value.Key == key).Key);

    public Guid? FindKey(int memberId) => _members.TryGetValue(memberId, out MemberDetails? member) ? member.Key : null;

    public Task<IReadOnlyList<MemberDetails>> GetAsync(IReadOnlyCollection<Guid> keys) =>
        Task.FromResult<IReadOnlyList<MemberDetails>>(_members.Values.Where(m => keys.Contains(m.Key)).ToList());
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>An activity store written the way a site would write one to share between servers: no cap, no pruning.</summary>
internal sealed class SharedActivityStore : ILiveViewActivityStore
{
    private readonly object _lock = new();
    private readonly Dictionary<(string Set, string Key), LiveViewActivity> _entries = new();

    /// <summary>The set asked for by each <see cref="GetActiveAsync"/> call.</summary>
    public List<string> GetActiveCalls { get; } = [];

    /// <summary>The sets asked for by each <see cref="CountActiveAsync"/> call, one list per call.</summary>
    public List<IReadOnlyList<string>> CountActiveCalls { get; } = [];

    public Task<bool> SeenAsync(string set, string key, DateTimeOffset at)
    {
        lock (_lock)
        {
            _entries[(set, key)] = _entries.TryGetValue((set, key), out LiveViewActivity? entry)
                ? entry with { Last = at }
                : new LiveViewActivity(key, at, at, 0);
        }

        return Task.FromResult(true);
    }

    public Task<bool> OpenedAsync(string set, string key, DateTimeOffset at)
    {
        lock (_lock)
        {
            LiveViewActivity entry = _entries.TryGetValue((set, key), out LiveViewActivity? found)
                ? found
                : new LiveViewActivity(key, at, at, 0);
            _entries[(set, key)] = entry with { Last = at, Open = entry.Open + 1 };
        }

        return Task.FromResult(true);
    }

    public Task ClosedAsync(string set, string key, DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue((set, key), out LiveViewActivity? entry))
            {
                _entries[(set, key)] = entry with { Last = at, Open = Math.Max(0, entry.Open - 1) };
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string set, string key)
    {
        lock (_lock)
        {
            _entries.Remove((set, key));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveViewActivity>> GetActiveAsync(string set, DateTimeOffset since)
    {
        lock (_lock)
        {
            GetActiveCalls.Add(set);
            return Task.FromResult(Active(set, since));
        }
    }

    public Task<IReadOnlyList<int>> CountActiveAsync(IReadOnlyList<string> sets, DateTimeOffset since)
    {
        lock (_lock)
        {
            CountActiveCalls.Add(sets);
            return Task.FromResult<IReadOnlyList<int>>(sets.Select(s => Active(s, since).Count).ToList());
        }
    }

    private IReadOnlyList<LiveViewActivity> Active(string set, DateTimeOffset since) =>
        _entries.Where(e => e.Key.Set == set && (e.Value.Open > 0 || e.Value.Last >= since)).Select(e => e.Value).ToList();
}

/// <summary>A message store written the way a site would write one to share between servers.</summary>
internal sealed class SharedBackofficeMessageStore : IBackofficeMessageStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, (BackofficeMessage Message, HashSet<Guid> Undelivered, HashSet<Guid> Recipients)> _messages = new();

    public Task AddAsync(BackofficeMessage message, IReadOnlyCollection<Guid> recipients, DateTimeOffset expiresAt)
    {
        lock (_lock)
        {
            _messages[message.Key] = (message, [.. recipients], [.. recipients]);
        }

        return Task.CompletedTask;
    }

    public Task<BackofficeMessage?> ReceiveAsync(Guid messageKey, Guid userKey)
    {
        lock (_lock)
        {
            if (!_messages.TryGetValue(messageKey, out var entry) || !entry.Recipients.Contains(userKey))
            {
                return Task.FromResult<BackofficeMessage?>(null);
            }

            entry.Undelivered.Remove(userKey);
            return Task.FromResult<BackofficeMessage?>(entry.Message);
        }
    }

    public Task<IReadOnlyList<Guid>> GetUndeliveredAsync(Guid userKey)
    {
        lock (_lock)
        {
            IReadOnlyList<Guid> keys = _messages.Values.Where(e => e.Undelivered.Contains(userKey)).Select(e => e.Message.Key).ToList();
            return Task.FromResult(keys);
        }
    }
}
