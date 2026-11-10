using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>How long a session counts: for the activity window after its last request or touch, while a request is open, until it is ended, and only while its channel has room.</summary>
public class LiveActivityTrackerTests
{
    private FakeTimeProvider _clock;
    private LiveViewOptions _options;
    private LiveActivityTracker _tracker;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _options = new LiveViewOptions { ActivityWindowSeconds = 300 };
        var options = new StaticOptionsMonitor<LiveViewOptions>(_options);
        _tracker = new LiveActivityTracker(new InMemoryActivityStore(options), options, _clock);
    }

    [Test]
    public async Task A_session_counts_until_the_window_after_its_last_request()
    {
        await RequestAsync("visitor");

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task Each_request_restarts_the_window()
    {
        await RequestAsync("visitor");
        _clock.Advance(TimeSpan.FromSeconds(200));
        await RequestAsync("visitor");

        _clock.Advance(TimeSpan.FromSeconds(200));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(101));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task Visitors_who_stop_are_no_longer_counted()
    {
        for (int i = 0; i < 150; i++)
        {
            await RequestAsync($"visitor {i}");
        }

        _clock.Advance(TimeSpan.FromMinutes(4));
        await RequestAsync("visitor 0");
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(150));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
    }

    [Test]
    public async Task An_open_request_counts_however_long_it_lasts()
    {
        IAsyncDisposable stream = await _tracker.BeginActivityAsync("web", "visitor");

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        await stream.DisposeAsync();
        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task Overlapping_requests_count_until_the_last_one_closes()
    {
        IAsyncDisposable first = await _tracker.BeginActivityAsync("web", "visitor");
        IAsyncDisposable second = await _tracker.BeginActivityAsync("web", "visitor");
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        await first.DisposeAsync();
        await first.DisposeAsync();
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        await second.DisposeAsync();
        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task A_touch_counts_a_session_for_the_window()
    {
        await _tracker.TouchAsync("web", "visitor");
        _clock.Advance(TimeSpan.FromSeconds(200));
        await _tracker.TouchAsync("web", "visitor");

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task A_touch_does_not_close_an_open_request()
    {
        await using IAsyncDisposable stream = await _tracker.BeginActivityAsync("web", "visitor");

        await _tracker.TouchAsync("web", "visitor");
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
    }

    [Test]
    public async Task Each_channel_counts_its_own_sessions()
    {
        _options.Channels["api"] = new LiveViewChannelOptions { ActivityWindowSeconds = 60 };
        await _tracker.TouchAsync("web", "visitor");
        await _tracker.TouchAsync("api", "visitor");
        Assert.That(await _tracker.CountActiveAsync("WEB"), Is.EqualTo(1));

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
        Assert.That(await _tracker.CountActiveAsync("api"), Is.Zero);
        Assert.That(await _tracker.CountActiveAsync("nobody uses this"), Is.Zero);
    }

    [Test]
    public async Task Ending_a_session_stops_counting_it_straight_away()
    {
        await _tracker.TouchAsync("web", "visitor");
        await _tracker.TouchAsync("web", "another visitor");
        await _tracker.TouchAsync("api", "visitor");

        await _tracker.EndAsync("web", "visitor");

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
        Assert.That(await _tracker.CountActiveAsync("api"), Is.EqualTo(1));
    }

    [Test]
    public async Task Ending_a_session_with_an_open_request_stops_counting_it()
    {
        IAsyncDisposable stream = await _tracker.BeginActivityAsync("web", "visitor");

        await _tracker.EndAsync("web", "visitor");
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);

        await stream.DisposeAsync();
        Assert.That(await _tracker.CountActiveAsync("web"), Is.Zero);
    }

    [Test]
    public async Task A_session_that_ended_counts_again_on_its_next_request()
    {
        await _tracker.TouchAsync("web", "visitor");
        await _tracker.EndAsync("web", "visitor");

        await RequestAsync("visitor");

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
    }

    [Test]
    public async Task Ending_a_session_that_is_not_tracked_does_nothing()
    {
        await _tracker.TouchAsync("web", "visitor");

        await _tracker.EndAsync("web", "stranger");
        await _tracker.EndAsync("nobody uses this", "visitor");

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_full_channel_ignores_new_sessions_but_keeps_counting_the_ones_it_has()
    {
        _options.MaxSessionsPerChannel = 2;
        await _tracker.TouchAsync("web", "first");
        await _tracker.TouchAsync("web", "second");

        _clock.Advance(TimeSpan.FromSeconds(200));
        await _tracker.TouchAsync("web", "first");
        await _tracker.TouchAsync("web", "third");
        await using IAsyncDisposable stream = await _tracker.BeginActivityAsync("web", "fourth");
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(2));

        _clock.Advance(TimeSpan.FromSeconds(200));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(1));
    }

    [Test]
    public async Task The_session_limit_applies_to_each_channel_separately()
    {
        _options.MaxSessionsPerChannel = 1;
        await _tracker.TouchAsync("web", "visitor");

        await _tracker.TouchAsync("api", "visitor");

        Assert.That(await _tracker.CountActiveAsync("api"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_full_channel_takes_new_sessions_once_old_ones_expire_while_the_dashboard_reads_it()
    {
        _options.MaxSessionsPerChannel = 2;
        await _tracker.TouchAsync("web", "first");
        await _tracker.TouchAsync("web", "second");

        // The dashboard reading the count in between does not stop a full channel making room.
        _clock.Advance(TimeSpan.FromSeconds(250));
        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(2));

        _clock.Advance(TimeSpan.FromSeconds(51));
        await _tracker.TouchAsync("web", "third");
        await _tracker.TouchAsync("web", "fourth");

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(2));
    }

    [Test]
    public async Task A_full_channel_takes_new_sessions_once_old_ones_expire()
    {
        _options.MaxSessionsPerChannel = 2;
        await _tracker.TouchAsync("web", "first");
        await _tracker.TouchAsync("web", "second");

        _clock.Advance(TimeSpan.FromSeconds(301));
        await _tracker.TouchAsync("web", "third");
        await _tracker.TouchAsync("web", "fourth");

        Assert.That(await _tracker.CountActiveAsync("web"), Is.EqualTo(2));
    }

    private async Task RequestAsync(string sessionKey)
    {
        IAsyncDisposable request = await _tracker.BeginActivityAsync("web", sessionKey);
        await request.DisposeAsync();
    }
}
