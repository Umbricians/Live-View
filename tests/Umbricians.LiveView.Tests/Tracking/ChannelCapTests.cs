using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

public class ChannelCapTests
{
    private FakeTimeProvider _clock;
    private StaticOptionsMonitor<LiveViewOptions> _options;
    private CountingStore _store;
    private LiveActivityTracker _tracker;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _options = new StaticOptionsMonitor<LiveViewOptions>(
            new LiveViewOptions { ActivityWindowSeconds = 60, MaxSessionsPerChannel = 2 });
        _store = new CountingStore(new InMemoryActivityStore(_options));
        _tracker = new LiveActivityTracker(_store, _options, _clock);
    }

    [Test]
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

        Assert.That(await _tracker.CountActiveAsync("custom"), Is.EqualTo(2));
        await _tracker.EndAsync("custom", "d");
        Assert.That(await _tracker.CountActiveAsync("custom"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_full_channel_is_pruned_at_most_once_a_second()
    {
        await _tracker.TouchAsync("custom", "a");
        await _tracker.TouchAsync("custom", "b");
        int afterFirstPrune = _store.Counts;

        await _tracker.TouchAsync("custom", "c");
        await _tracker.TouchAsync("custom", "d");
        Assert.That(_store.Counts, Is.EqualTo(afterFirstPrune));

        _clock.Advance(TimeSpan.FromSeconds(2));
        await _tracker.TouchAsync("custom", "e");
        Assert.That(_store.Counts, Is.EqualTo(afterFirstPrune + 1));

        await _tracker.TouchAsync("custom", "f");
        Assert.That(_store.Counts, Is.EqualTo(afterFirstPrune + 1));
    }

    [Test]
    public async Task A_store_without_a_cap_is_not_pruned_for_a_full_channel()
    {
        var uncapped = new SharedActivityStore();
        var tracker = new LiveActivityTracker(uncapped, _options, _clock);

        await tracker.TouchAsync("custom", "a");
        await tracker.TouchAsync("custom", "b");
        _clock.Advance(TimeSpan.FromSeconds(5));
        await tracker.TouchAsync("custom", "c");

        Assert.That(uncapped.CountActiveCalls, Has.Exactly(1).Items);
        Assert.That(await tracker.CountActiveAsync("custom"), Is.EqualTo(3));
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
