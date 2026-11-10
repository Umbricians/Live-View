using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>How long a session counts: for the activity window after its last request or touch, while a request is open, until it is ended, and only while its channel has room.</summary>
public class LiveActivityTrackerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly LiveViewOptions _options = new() { ActivityWindowSeconds = 300 };
    private readonly LiveActivityTracker _tracker;

    public LiveActivityTrackerTests() =>
        _tracker = new LiveActivityTracker(new StaticOptionsMonitor<LiveViewOptions>(_options), _clock);

    [Fact]
    public void A_session_counts_until_the_window_after_its_last_request()
    {
        Request("visitor");

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(1, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void Each_request_restarts_the_window()
    {
        Request("visitor");
        _clock.Advance(TimeSpan.FromSeconds(200));
        Request("visitor");

        _clock.Advance(TimeSpan.FromSeconds(200));
        Assert.Equal(1, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromSeconds(101));
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void Visitors_who_stop_are_no_longer_counted()
    {
        for (int i = 0; i < 150; i++)
        {
            Request($"visitor {i}");
        }

        _clock.Advance(TimeSpan.FromMinutes(4));
        Request("visitor 0");
        Assert.Equal(150, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, _tracker.CountActive("web"));
    }

    [Fact]
    public void An_open_request_counts_however_long_it_lasts()
    {
        IDisposable stream = _tracker.BeginActivity("web", "visitor");

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, _tracker.CountActive("web"));

        stream.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void Overlapping_requests_count_until_the_last_one_closes()
    {
        IDisposable first = _tracker.BeginActivity("web", "visitor");
        IDisposable second = _tracker.BeginActivity("web", "visitor");
        Assert.Equal(1, _tracker.CountActive("web"));

        first.Dispose();
        first.Dispose();
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, _tracker.CountActive("web"));

        second.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(301));
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void A_touch_counts_a_session_for_the_window()
    {
        _tracker.Touch("web", "visitor");
        _clock.Advance(TimeSpan.FromSeconds(200));
        _tracker.Touch("web", "visitor");

        _clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(1, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void A_touch_does_not_close_an_open_request()
    {
        using IDisposable stream = _tracker.BeginActivity("web", "visitor");

        _tracker.Touch("web", "visitor");
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, _tracker.CountActive("web"));
    }

    [Fact]
    public void Each_channel_counts_its_own_sessions()
    {
        _options.Channels["api"] = new LiveViewChannelOptions { ActivityWindowSeconds = 60 };
        _tracker.Touch("web", "visitor");
        _tracker.Touch("api", "visitor");
        Assert.Equal(1, _tracker.CountActive("WEB"));

        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(1, _tracker.CountActive("web"));
        Assert.Equal(0, _tracker.CountActive("api"));
        Assert.Equal(0, _tracker.CountActive("nobody uses this"));
    }

    [Fact]
    public void Ending_a_session_stops_counting_it_straight_away()
    {
        _tracker.Touch("web", "visitor");
        _tracker.Touch("web", "another visitor");
        _tracker.Touch("api", "visitor");

        _tracker.End("web", "visitor");

        Assert.Equal(1, _tracker.CountActive("web"));
        Assert.Equal(1, _tracker.CountActive("api"));
    }

    [Fact]
    public void Ending_a_session_with_an_open_request_stops_counting_it()
    {
        IDisposable stream = _tracker.BeginActivity("web", "visitor");

        _tracker.End("web", "visitor");
        Assert.Equal(0, _tracker.CountActive("web"));

        stream.Dispose();
        Assert.Equal(0, _tracker.CountActive("web"));
    }

    [Fact]
    public void A_session_that_ended_counts_again_on_its_next_request()
    {
        _tracker.Touch("web", "visitor");
        _tracker.End("web", "visitor");

        Request("visitor");

        Assert.Equal(1, _tracker.CountActive("web"));
    }

    [Fact]
    public void Ending_a_session_that_is_not_tracked_does_nothing()
    {
        _tracker.Touch("web", "visitor");

        _tracker.End("web", "stranger");
        _tracker.End("nobody uses this", "visitor");

        Assert.Equal(1, _tracker.CountActive("web"));
    }

    [Fact]
    public void A_full_channel_ignores_new_sessions_but_keeps_counting_the_ones_it_has()
    {
        _options.MaxSessionsPerChannel = 2;
        _tracker.Touch("web", "first");
        _tracker.Touch("web", "second");

        _clock.Advance(TimeSpan.FromSeconds(200));
        _tracker.Touch("web", "first");
        _tracker.Touch("web", "third");
        using IDisposable stream = _tracker.BeginActivity("web", "fourth");
        Assert.Equal(2, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromSeconds(200));
        Assert.Equal(1, _tracker.CountActive("web"));
    }

    [Fact]
    public void The_session_limit_applies_to_each_channel_separately()
    {
        _options.MaxSessionsPerChannel = 1;
        _tracker.Touch("web", "visitor");

        _tracker.Touch("api", "visitor");

        Assert.Equal(1, _tracker.CountActive("api"));
    }

    [Fact]
    public void A_full_channel_takes_new_sessions_once_old_ones_expire_while_the_dashboard_reads_it()
    {
        _options.MaxSessionsPerChannel = 2;
        _tracker.Touch("web", "first");
        _tracker.Touch("web", "second");

        // The dashboard reading the count in between does not stop a full channel making room.
        _clock.Advance(TimeSpan.FromSeconds(250));
        Assert.Equal(2, _tracker.CountActive("web"));

        _clock.Advance(TimeSpan.FromSeconds(51));
        _tracker.Touch("web", "third");
        _tracker.Touch("web", "fourth");

        Assert.Equal(2, _tracker.CountActive("web"));
    }

    [Fact]
    public void A_full_channel_takes_new_sessions_once_old_ones_expire()
    {
        _options.MaxSessionsPerChannel = 2;
        _tracker.Touch("web", "first");
        _tracker.Touch("web", "second");

        _clock.Advance(TimeSpan.FromSeconds(301));
        _tracker.Touch("web", "third");
        _tracker.Touch("web", "fourth");

        Assert.Equal(2, _tracker.CountActive("web"));
    }

    private void Request(string sessionKey) => _tracker.BeginActivity("web", sessionKey).Dispose();
}
