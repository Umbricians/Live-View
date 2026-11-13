using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Storage;

public class InMemoryActivityStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private LiveViewOptions _options;
    private InMemoryActivityStore _store;

    [SetUp]
    public void SetUp()
    {
        _options = new LiveViewOptions { MaxSessionsPerChannel = 2 };
        _store = new InMemoryActivityStore(new StaticOptionsMonitor<LiveViewOptions>(_options));
    }

    private async Task<int> CountAsync(string set, DateTimeOffset since) => (await _store.CountActiveAsync([set], since))[0];

    [Test]
    public async Task New_keys_are_dropped_at_the_cap_and_existing_keys_are_still_updated()
    {
        await _store.SeenAsync("set", "a", Start);
        await _store.SeenAsync("set", "b", Start);
        await _store.SeenAsync("set", "c", Start);
        await _store.OpenedAsync("set", "d", Start);
        DateTimeOffset later = Start.AddMinutes(1);

        await _store.SeenAsync("set", "a", later);

        IReadOnlyList<LiveViewActivity> active = await _store.GetActiveAsync("set", Start);
        Assert.That(active.Select(e => e.Key).Order(), Is.EqualTo(["a", "b"]));
        Assert.That(active.Single(e => e.Key == "a").Last, Is.EqualTo(later));
        Assert.That(active.Single(e => e.Key == "a").First, Is.EqualTo(Start));
    }

    [Test]
    public async Task Seen_and_opened_say_whether_the_entry_was_kept()
    {
        Assert.That(await _store.SeenAsync("set", "a", Start), Is.True);
        Assert.That(await _store.OpenedAsync("set", "b", Start), Is.True);

        Assert.That(await _store.SeenAsync("set", "c", Start), Is.False);
        Assert.That(await _store.OpenedAsync("set", "d", Start), Is.False);
        Assert.That(await _store.SeenAsync("set", "a", Start.AddSeconds(1)), Is.True);
        Assert.That(await _store.OpenedAsync("set", "b", Start.AddSeconds(1)), Is.True);
    }

    [Test]
    public async Task The_cap_applies_to_each_set_on_its_own()
    {
        await _store.SeenAsync("one", "a", Start);
        await _store.SeenAsync("one", "b", Start);
        await _store.SeenAsync("two", "a", Start);

        Assert.That(await CountAsync("one", Start), Is.EqualTo(2));
        Assert.That(await CountAsync("two", Start), Is.EqualTo(1));
    }

    [Test]
    public async Task Counting_several_sets_gives_one_count_each_in_the_order_given()
    {
        await _store.SeenAsync("one", "a", Start);
        await _store.SeenAsync("one", "b", Start);
        await _store.SeenAsync("two", "a", Start);

        IReadOnlyList<int> counts = await _store.CountActiveAsync(["two", "unknown", "one"], Start);

        Assert.That(counts, Is.EqualTo([1, 0, 2]));
    }

    [Test]
    public async Task Closed_on_a_removed_entry_does_not_bring_it_back()
    {
        await _store.OpenedAsync("set", "a", Start);
        await _store.RemoveAsync("set", "a");

        await _store.ClosedAsync("set", "a", Start.AddSeconds(1));

        Assert.That(await CountAsync("set", DateTimeOffset.MinValue), Is.Zero);
    }

    [Test]
    public async Task Open_entries_are_never_pruned()
    {
        await _store.OpenedAsync("set", "a", Start);
        await _store.SeenAsync("set", "b", Start);
        DateTimeOffset cutoff = Start.AddHours(1);

        Assert.That((await _store.GetActiveAsync("set", cutoff)).Select(e => e.Key), Is.EqualTo(["a"]));

        await _store.ClosedAsync("set", "a", Start.AddMinutes(1));
        Assert.That(await CountAsync("set", cutoff), Is.Zero);
    }

    [Test]
    public async Task An_entry_stays_active_until_every_open_request_has_closed()
    {
        await _store.OpenedAsync("set", "a", Start);
        await _store.OpenedAsync("set", "a", Start);
        DateTimeOffset cutoff = Start.AddHours(1);

        await _store.ClosedAsync("set", "a", Start.AddMinutes(1));
        Assert.That(await CountAsync("set", cutoff), Is.EqualTo(1));

        await _store.ClosedAsync("set", "a", Start.AddMinutes(2));
        await _store.ClosedAsync("set", "a", Start.AddMinutes(3));
        Assert.That(await CountAsync("set", cutoff), Is.Zero);
    }
}
