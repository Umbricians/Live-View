using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tracking;

internal sealed class LiveActivityTracker : ILiveActivityTracker
{
    private readonly ConcurrentDictionary<string, PruneState> _pruning = new();
    private readonly ILiveViewActivityStore _store;
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public LiveActivityTracker(
        ILiveViewActivityStore store, IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task TouchAsync(string channel, string sessionKey)
    {
        string set = ActivitySets.Channel(channel);
        string key = Hash(sessionKey);
        await WriteAsync(channel, set, now => _store.SeenAsync(set, key, now));
    }

    public async Task<IAsyncDisposable> BeginActivityAsync(string channel, string sessionKey)
    {
        string set = ActivitySets.Channel(channel);
        string key = Hash(sessionKey);
        await WriteAsync(channel, set, now => _store.OpenedAsync(set, key, now));
        return new Activity(this, set, key);
    }

    public Task EndAsync(string channel, string sessionKey) =>
        _store.RemoveAsync(ActivitySets.Channel(channel), Hash(sessionKey));

    public async Task<int> CountActiveAsync(string channel)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return (await _store.CountActiveAsync([ActivitySets.Channel(channel)], now - WindowFor(channel)))[0];
    }

    private TimeSpan WindowFor(string channel) => ChannelSettings.Of(_options.CurrentValue, channel).ActivityWindow;

    // Prunes from the write path too, so memory stays bounded when nobody is watching the dashboard. When the store
    // drops a new session because the channel is full, finished sessions are dropped (at most once a second) and the
    // write is tried once more, so a new session gets in as soon as a finished one has expired.
    private async Task WriteAsync(string channel, string set, Func<DateTimeOffset, Task<bool>> write)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan window = WindowFor(channel);
        PruneState state = _pruning.GetOrAdd(set, static _ => new PruneState());
        if (now.UtcTicks - Interlocked.Read(ref state.LastPruned) > window.Ticks)
        {
            await PruneAsync(set, state, now, window);
        }

        if (await write(now))
        {
            return;
        }

        if (now.UtcTicks - Interlocked.Read(ref state.LastPruned) > TimeSpan.TicksPerSecond)
        {
            await PruneAsync(set, state, now, window);
            await write(now);
        }
    }

    private async Task PruneAsync(string set, PruneState state, DateTimeOffset now, TimeSpan window)
    {
        Interlocked.Exchange(ref state.LastPruned, now.UtcTicks);
        await _store.CountActiveAsync([set], now - window);
    }

    // Session keys may be tokens or IP addresses, so only a truncated hash is kept in memory.
    private static string Hash(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return Convert.ToHexString(hash[..12]);
    }

    /// <summary>When a channel's store was last asked to drop finished sessions.</summary>
    private sealed class PruneState
    {
        public long LastPruned;
    }

    private sealed class Activity(LiveActivityTracker tracker, string set, string key) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await tracker._store.ClosedAsync(set, key, tracker._timeProvider.GetUtcNow());
            }
        }
    }
}
