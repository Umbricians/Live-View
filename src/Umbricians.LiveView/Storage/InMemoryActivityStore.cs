using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Storage;

internal sealed class InMemoryActivityStore : ILiveViewActivityStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _sets = new();
    private readonly IOptionsMonitor<LiveViewOptions> _options;

    public InMemoryActivityStore(IOptionsMonitor<LiveViewOptions> options)
    {
        _options = options;
    }

    public void Seen(string set, string key, DateTimeOffset at) => Record(set, key, at, open: false);

    public void Opened(string set, string key, DateTimeOffset at) => Record(set, key, at, open: true);

    public void Closed(string set, string key, DateTimeOffset at)
    {
        if (_sets.TryGetValue(set, out ConcurrentDictionary<string, Entry>? entries) && entries.TryGetValue(key, out Entry? entry))
        {
            entry.Last = at.UtcTicks;
            entry.Close();
        }
    }

    public void Remove(string set, string key)
    {
        if (_sets.TryGetValue(set, out ConcurrentDictionary<string, Entry>? entries))
        {
            entries.TryRemove(key, out _);
        }
    }

    public IReadOnlyList<LiveViewActivity> GetActive(string set, DateTimeOffset since) =>
        Prune(set, since, out ConcurrentDictionary<string, Entry>? entries)
            ? entries.Select(e => e.Value.ToActivity(e.Key)).ToList()
            : [];

    public int CountActive(string set, DateTimeOffset since) =>
        Prune(set, since, out ConcurrentDictionary<string, Entry>? entries) ? entries.Count : 0;

    private void Record(string set, string key, DateTimeOffset at, bool open)
    {
        while (true)
        {
            ConcurrentDictionary<string, Entry> entries = _sets.GetOrAdd(set, static _ => new ConcurrentDictionary<string, Entry>());
            if (!entries.TryGetValue(key, out Entry? entry))
            {
                // At capacity, existing entries stay tracked but new ones are dropped rather than growing without bound.
                if (entries.Count >= _options.CurrentValue.MaxSessionsPerChannel)
                {
                    return;
                }

                entry = entries.GetOrAdd(key, static (_, ticks) => new Entry(ticks), at.UtcTicks);
            }

            entry.Last = at.UtcTicks;
            if (open)
            {
                entry.Open();
            }

            // The set may have been removed as empty while this was added to it; if so, add to the current one.
            if (_sets.TryGetValue(set, out ConcurrentDictionary<string, Entry>? current) && current == entries)
            {
                return;
            }

            if (open)
            {
                entry.Close();
            }
        }
    }

    // Drops the entries with nothing open that are past the cutoff, and the set once it is empty.
    private bool Prune(string set, DateTimeOffset since, [NotNullWhen(true)] out ConcurrentDictionary<string, Entry>? entries)
    {
        if (!_sets.TryGetValue(set, out entries))
        {
            return false;
        }

        long cutoff = since.UtcTicks;
        foreach ((string key, Entry entry) in entries)
        {
            if (!entry.IsOpen && entry.Last < cutoff)
            {
                entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }

        if (entries.IsEmpty)
        {
            _sets.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, Entry>>(set, entries));
            return false;
        }

        return true;
    }

    private sealed class Entry(long ticks)
    {
        private readonly long _first = ticks;
        private long _last = ticks;
        private int _open;

        public long Last
        {
            get => Interlocked.Read(ref _last);
            set => Interlocked.Exchange(ref _last, value);
        }

        public bool IsOpen => Volatile.Read(ref _open) > 0;

        public void Open() => Interlocked.Increment(ref _open);

        public void Close()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _open);
            }
            while (current > 0 && Interlocked.CompareExchange(ref _open, current - 1, current) != current);
        }

        public LiveViewActivity ToActivity(string key) =>
            new(key, new DateTimeOffset(_first, TimeSpan.Zero), new DateTimeOffset(Last, TimeSpan.Zero), Volatile.Read(ref _open));
    }
}
