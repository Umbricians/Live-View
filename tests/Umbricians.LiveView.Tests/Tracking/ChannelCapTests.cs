using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

public class ChannelCapTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly StaticOptionsMonitor<LiveViewOptions> _options = new(
        new LiveViewOptions { ActivityWindowSeconds = 60, MaxSessionsPerChannel = 2 });
    private readonly CountingStore _store;
    private readonly LiveActivityTracker _tracker;

    public ChannelCapTests()
    {
        _store = new CountingStore(new InMemoryActivityStore(_options));
        _tracker = new LiveActivityTracker(_store, _options, _clock);
    }

    [Fact]
    public async Task A_new_session_gets_in_as_soon_as_a_finished_one_has_expired()
    {
        await _tracker.TouchAsync("custom", "a");
        _clock.Advance(TimeSpan.FromSeconds(59));
        await _tracker.TouchAsync("custom", "b");
        _clock.Advance(TimeSpan.FromSeconds(2));
        await _tracker.TouchAsync("custom", "c");

        // Two minutes in, b has expired but nothing has dropped it, so the channel looks full of b and c.
        _clock.Advance(TimeSpan.FromSeconds(59));
        await _tracker.TouchAsync("custom", "d");

        Assert.Equal(2, await _tracker.CountActiveAsync("custom"));
        await _tracker.EndAsync("custom", "d");
        Assert.Equal(1, await _tracker.CountActiveAsync("custom"));
    }

    [Fact]
    public async Task A_full_channel_is_pruned_at_most_once_a_second()
    {
        await _tracker.TouchAsync("custom", "a");
        await _tracker.TouchAsync("custom", "b");
        int afterFirstPrune = _store.Counts;

        await _tracker.TouchAsync("custom", "c");
        await _tracker.TouchAsync("custom", "d");
        Assert.Equal(afterFirstPrune, _store.Counts);

        _clock.Advance(TimeSpan.FromSeconds(2));
        await _tracker.TouchAsync("custom", "e");
        Assert.Equal(afterFirstPrune + 1, _store.Counts);

        await _tracker.TouchAsync("custom", "f");
        Assert.Equal(afterFirstPrune + 1, _store.Counts);
    }

    [Fact]
    public async Task A_store_without_a_cap_is_not_pruned_for_a_full_channel()
    {
        var uncapped = new SharedActivityStore();
        var tracker = new LiveActivityTracker(uncapped, _options, _clock);

        await tracker.TouchAsync("custom", "a");
        await tracker.TouchAsync("custom", "b");
        _clock.Advance(TimeSpan.FromSeconds(5));
        await tracker.TouchAsync("custom", "c");

        Assert.Single(uncapped.CountActiveCalls);
        Assert.Equal(3, await tracker.CountActiveAsync("custom"));
    }

    private sealed class CountingStore(ILiveViewActivityStore inner) : ILiveViewActivityStore
    {
        /// <summary>How many times counts have been read, which is also what prunes a set.</summary>
        public int Counts { get; private set; }

        public Task<bool> SeenAsync(string set, string key, DateTimeOffset at) => inner.SeenAsync(set, key, at);

        public Task<bool> OpenedAsync(string set, string key, DateTimeOffset at) => inner.OpenedAsync(set, key, at);

        public Task ClosedAsync(string set, string key, DateTimeOffset at) => inner.ClosedAsync(set, key, at);

        public Task RemoveAsync(string set, string key) => inner.RemoveAsync(set, key);

        public Task<IReadOnlyList<LiveViewActivity>> GetActiveAsync(string set, DateTimeOffset since) => inner.GetActiveAsync(set, since);

        public Task<IReadOnlyList<int>> CountActiveAsync(IReadOnlyList<string> sets, DateTimeOffset since)
        {
            Counts++;
            return inner.CountActiveAsync(sets, since);
        }
    }
}
