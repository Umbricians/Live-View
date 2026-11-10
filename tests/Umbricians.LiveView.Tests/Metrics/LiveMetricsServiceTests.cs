using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Metrics;

/// <summary>The lines of a snapshot, including those of metric providers, which take their settings by alias.</summary>
public class LiveMetricsServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly LiveViewOptions _options = new();
    private readonly StubMetricProvider _queue = new("queue", "Queue length", "#8b5cf6", value: 7);

    [Fact]
    public async Task A_provider_line_shows_the_providers_own_values_when_nothing_is_configured()
    {
        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        LiveMetricReading queue = Assert.Single(snapshot, r => r.Alias == "queue");
        Assert.Equal("Queue length", queue.Name);
        Assert.Equal("#8b5cf6", queue.Color);
        Assert.Equal(7, queue.Value);
    }

    [Fact]
    public async Task Name_colour_and_sort_order_under_the_providers_alias_replace_its_own()
    {
        _options.Channels["web"] = new LiveViewChannelOptions { SortOrder = 100 };
        _options.Channels["queue"] = new LiveViewChannelOptions { Name = "Queued work", Color = "#000", SortOrder = 5 };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.Equal(["queue", "web"], snapshot.Select(r => r.Alias).Where(a => a is "queue" or "web"));
        LiveMetricReading queue = Assert.Single(snapshot, r => r.Alias == "queue");
        Assert.Equal("Queued work", queue.Name);
        Assert.Equal("#000", queue.Color);
        Assert.Equal(7, queue.Value);
    }

    [Fact]
    public async Task Values_left_out_keep_the_providers_own()
    {
        _options.Channels["web"] = new LiveViewChannelOptions { SortOrder = 100 };
        _options.Channels["queue"] = new LiveViewChannelOptions { Color = "#000" };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.Equal(["web", "queue"], snapshot.Select(r => r.Alias).Where(a => a is "queue" or "web"));
        LiveMetricReading queue = Assert.Single(snapshot, r => r.Alias == "queue");
        Assert.Equal("Queue length", queue.Name);
        Assert.Equal("#000", queue.Color);
    }

    [Fact]
    public async Task A_switched_off_provider_line_is_dropped_and_the_provider_is_not_called()
    {
        _options.Channels["queue"] = new LiveViewChannelOptions { Enabled = false };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.DoesNotContain(snapshot, r => r.Alias == "queue");
        Assert.Equal(0, _queue.Calls);
    }

    [Fact]
    public async Task Settings_under_a_providers_alias_add_no_channel_line()
    {
        _options.Channels["QUEUE"] = new LiveViewChannelOptions { Name = "Queued work", PathPrefixes = ["/"] };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        LiveMetricReading line = Assert.Single(snapshot, r => r.Alias.Equals("queue", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queue", line.Alias);
        Assert.Equal("Queued work", line.Name);
    }

    [Fact]
    public async Task A_provider_aliased_like_a_built_in_channel_replaces_it()
    {
        var api = new StubMetricProvider("api", "Queue depth", value: 4);
        new LiveViewOptionsDefaults([]).PostConfigure(null, _options);

        await AssertReplacedByProvider(api, "/api/products");
    }

    [Fact]
    public async Task A_provider_aliased_like_a_channel_declared_in_code_replaces_it()
    {
        var shop = new StubMetricProvider("shop", "Basket size", value: 4);
        new LiveViewOptionsDefaults([new LiveViewChannelRegistration("shop", new() { PathPrefixes = ["/shop"] })])
            .PostConfigure(null, _options);

        await AssertReplacedByProvider(shop, "/shop/basket");
    }

    // The request is matched by no channel under the provider's alias, and the snapshot has one line for it, read from the provider.
    private async Task AssertReplacedByProvider(StubMetricProvider provider, string path)
    {
        var monitor = new StaticOptionsMonitor<LiveViewOptions>(_options);
        var classifier = new RequestClassifier(monitor, [], [provider]);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        Assert.DoesNotContain(provider.Alias, classifier.Classify(context.Request).Sessions.Select(s => s.Channel));

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot(provider);

        LiveMetricReading line = Assert.Single(snapshot, r => r.Alias == provider.Alias);
        Assert.Equal(4, line.Value);
        Assert.Equal(1, provider.Calls);
    }

    private Task<IReadOnlyList<LiveMetricReading>> Snapshot(params ILiveMetricProvider[] providers)
    {
        var monitor = new StaticOptionsMonitor<LiveViewOptions>(_options);
        var store = new InMemoryActivityStore(monitor);
        return new LiveMetricsService(
            new LiveActivityTracker(store, monitor, _clock),
            new BackofficePresence(store, new InMemoryBackofficeUserDirectory(), monitor, _clock),
            new MemberPresence(store, new InMemoryMemberDirectory(), monitor, _clock),
            providers.Length > 0 ? providers : [_queue],
            monitor,
            NullLogger<LiveMetricsService>.Instance).GetSnapshotAsync(CancellationToken.None);
    }
}
