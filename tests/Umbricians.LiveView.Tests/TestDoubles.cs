using Microsoft.Extensions.Options;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tests;

internal sealed class InMemoryBackofficeUserDirectory : IBackofficeUserDirectory
{
    private readonly Dictionary<Guid, BackofficeUserDetails> _users = new();

    public Guid Add(string name, string? email = null)
    {
        var key = Guid.NewGuid();
        _users[key] = new BackofficeUserDetails(key, name, email, 0);
        return key;
    }

    /// <summary>Stands in for a user who is deleted or disabled: either way they are no longer found.</summary>
    public void Remove(Guid key) => _users.Remove(key);

    public void SetConnections(Guid key, int connections) => _users[key] = _users[key] with { Connections = connections };

    public Task<IReadOnlyList<BackofficeUserDetails>> GetAsync(IReadOnlyCollection<Guid> keys) =>
        Task.FromResult<IReadOnlyList<BackofficeUserDetails>>(_users.Values.Where(u => keys.Contains(u.Key)).ToList());
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
