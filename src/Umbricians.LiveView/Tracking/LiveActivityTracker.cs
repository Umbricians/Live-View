using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Tracking;

internal sealed class LiveActivityTracker : ILiveActivityTracker
{
    private readonly ConcurrentDictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly TimeProvider _timeProvider;

    public LiveActivityTracker(IOptionsMonitor<LiveViewOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public void Touch(string channel, string sessionKey)
    {
        if (TryGetSession(channel, sessionKey, out Session? session))
        {
            session.LastSeen = Now;
        }
    }

    public IDisposable BeginActivity(string channel, string sessionKey)
    {
        if (!TryGetSession(channel, sessionKey, out Session? session))
        {
            return NoActivity.Instance;
        }

        Interlocked.Increment(ref session.OpenActivities);
        session.LastSeen = Now;
        return new Activity(this, session);
    }

    public void End(string channel, string sessionKey)
    {
        if (_channels.TryGetValue(channel, out Channel? state))
        {
            state.Sessions.TryRemove(Hash(sessionKey), out _);
        }
    }

    public int CountActive(string channel)
    {
        if (!_channels.TryGetValue(channel, out Channel? state))
        {
            return 0;
        }

        long now = Now;
        state.Prune(now, now - WindowFor(channel));
        return state.Sessions.Count;
    }

    private long Now => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private long WindowFor(string channel) =>
        (long)ChannelSettings.Of(_options.CurrentValue, channel).ActivityWindow.TotalMilliseconds;

    private bool TryGetSession(string channel, string sessionKey, [NotNullWhen(true)] out Session? session)
    {
        Channel state = _channels.GetOrAdd(channel, static _ => new Channel());
        string key = Hash(sessionKey);
        if (state.Sessions.TryGetValue(key, out session))
        {
            return true;
        }

        // Prune from the write path too, so memory stays bounded when nobody is watching the dashboard.
        long now = Now;
        long window = WindowFor(channel);
        int max = _options.CurrentValue.MaxSessionsPerChannel;
        long sinceLastPrune = now - Interlocked.Read(ref state.LastPruned);
        if (sinceLastPrune > window || (state.Sessions.Count >= max && sinceLastPrune > 1000))
        {
            state.Prune(now, now - window);
        }

        // At capacity, existing sessions stay tracked but new ones are dropped rather than growing without bound.
        if (state.Sessions.Count >= max)
        {
            return false;
        }

        session = state.Sessions.GetOrAdd(key, static (_, n) => new Session { LastSeen = n }, now);
        return true;
    }

    // Session keys may be tokens or IP addresses, so only a truncated hash is kept in memory.
    private static string Hash(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return Convert.ToHexString(hash[..12]);
    }

    private sealed class Channel
    {
        public readonly ConcurrentDictionary<string, Session> Sessions = new();
        public long LastPruned;

        public void Prune(long now, long cutoff)
        {
            Interlocked.Exchange(ref LastPruned, now);
            foreach ((string key, Session session) in Sessions)
            {
                if (Volatile.Read(ref session.OpenActivities) <= 0 && session.LastSeen < cutoff)
                {
                    Sessions.TryRemove(new KeyValuePair<string, Session>(key, session));
                }
            }
        }
    }

    private sealed class Session
    {
        public int OpenActivities;
        private long _lastSeen;

        public long LastSeen
        {
            get => Interlocked.Read(ref _lastSeen);
            set => Interlocked.Exchange(ref _lastSeen, value);
        }
    }

    private sealed class Activity(LiveActivityTracker tracker, Session session) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                session.LastSeen = tracker.Now;
                Interlocked.Decrement(ref session.OpenActivities);
            }
        }
    }

    private sealed class NoActivity : IDisposable
    {
        public static readonly NoActivity Instance = new();

        public void Dispose()
        {
        }
    }
}
