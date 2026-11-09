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

    public void Touch(string channel, string sessionKey)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string set = ActivitySets.Channel(channel);
        PruneIfDue(channel, set, now);
        _store.Seen(set, Hash(sessionKey), now);
    }

    public IDisposable BeginActivity(string channel, string sessionKey)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string set = ActivitySets.Channel(channel);
        string key = Hash(sessionKey);
        PruneIfDue(channel, set, now);
        _store.Opened(set, key, now);
        return new Activity(this, set, key);
    }

    public void End(string channel, string sessionKey) =>
        _store.Remove(ActivitySets.Channel(channel), Hash(sessionKey));

    public int CountActive(string channel)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return _store.CountActive(ActivitySets.Channel(channel), now - WindowFor(channel));
    }

    private TimeSpan WindowFor(string channel) => ChannelSettings.Of(_options.CurrentValue, channel).ActivityWindow;

    // Prune from the write path too, so memory stays bounded when nobody is watching the dashboard.
    private void PruneIfDue(string channel, string set, DateTimeOffset now)
    {
        PruneState state = _pruning.GetOrAdd(set, static _ => new PruneState());
        TimeSpan window = WindowFor(channel);
        long sinceLastPrune = now.UtcTicks - Interlocked.Read(ref state.LastPruned);
        bool full = Volatile.Read(ref state.LastCount) >= _options.CurrentValue.MaxSessionsPerChannel;
        if (sinceLastPrune > window.Ticks || (full && sinceLastPrune > TimeSpan.TicksPerSecond))
        {
            Interlocked.Exchange(ref state.LastPruned, now.UtcTicks);
            Volatile.Write(ref state.LastCount, _store.CountActive(set, now - window));
        }
    }

    // Session keys may be tokens or IP addresses, so only a truncated hash is kept in memory.
    private static string Hash(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return Convert.ToHexString(hash[..12]);
    }

    /// <summary>When a channel's store was last asked to drop finished sessions, and how many it then held.</summary>
    private sealed class PruneState
    {
        public long LastPruned;
        public int LastCount;
    }

    private sealed class Activity(LiveActivityTracker tracker, string set, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                tracker._store.Closed(set, key, tracker._timeProvider.GetUtcNow());
            }
        }
    }
}
