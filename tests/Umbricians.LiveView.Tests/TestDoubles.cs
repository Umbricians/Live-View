using Microsoft.Extensions.Options;
using Umbricians.LiveView.Presence;

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
