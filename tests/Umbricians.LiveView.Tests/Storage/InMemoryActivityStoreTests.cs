using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Storage;

public class InMemoryActivityStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private readonly LiveViewOptions _options = new() { MaxSessionsPerChannel = 2 };
    private readonly InMemoryActivityStore _store;

    public InMemoryActivityStoreTests() => _store = new InMemoryActivityStore(new StaticOptionsMonitor<LiveViewOptions>(_options));

    [Fact]
    public void New_keys_are_dropped_at_the_cap_and_existing_keys_are_still_updated()
    {
        _store.Seen("set", "a", Start);
        _store.Seen("set", "b", Start);
        _store.Seen("set", "c", Start);
        _store.Opened("set", "d", Start);
        DateTimeOffset later = Start.AddMinutes(1);

        _store.Seen("set", "a", later);

        IReadOnlyList<LiveViewActivity> active = _store.GetActive("set", Start);
        Assert.Equal(["a", "b"], active.Select(e => e.Key).Order());
        Assert.Equal(later, active.Single(e => e.Key == "a").Last);
        Assert.Equal(Start, active.Single(e => e.Key == "a").First);
    }

    [Fact]
    public void The_cap_applies_to_each_set_on_its_own()
    {
        _store.Seen("one", "a", Start);
        _store.Seen("one", "b", Start);
        _store.Seen("two", "a", Start);

        Assert.Equal(2, _store.CountActive("one", Start));
        Assert.Equal(1, _store.CountActive("two", Start));
    }

    [Fact]
    public void Closed_on_a_removed_entry_does_not_bring_it_back()
    {
        _store.Opened("set", "a", Start);
        _store.Remove("set", "a");

        _store.Closed("set", "a", Start.AddSeconds(1));

        Assert.Equal(0, _store.CountActive("set", DateTimeOffset.MinValue));
    }

    [Fact]
    public void Open_entries_are_never_pruned()
    {
        _store.Opened("set", "a", Start);
        _store.Seen("set", "b", Start);
        DateTimeOffset cutoff = Start.AddHours(1);

        Assert.Equal(["a"], _store.GetActive("set", cutoff).Select(e => e.Key));

        _store.Closed("set", "a", Start.AddMinutes(1));
        Assert.Equal(0, _store.CountActive("set", cutoff));
    }

    [Fact]
    public void An_entry_stays_active_until_every_open_request_has_closed()
    {
        _store.Opened("set", "a", Start);
        _store.Opened("set", "a", Start);
        DateTimeOffset cutoff = Start.AddHours(1);

        _store.Closed("set", "a", Start.AddMinutes(1));
        Assert.Equal(1, _store.CountActive("set", cutoff));

        _store.Closed("set", "a", Start.AddMinutes(2));
        _store.Closed("set", "a", Start.AddMinutes(3));
        Assert.Equal(0, _store.CountActive("set", cutoff));
    }
}
