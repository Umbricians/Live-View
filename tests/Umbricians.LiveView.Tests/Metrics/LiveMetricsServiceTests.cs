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
    private FakeTimeProvider _clock;
    private LiveViewOptions _options;
    private StubMetricProvider _queue;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        _options = new LiveViewOptions();
        _queue = new StubMetricProvider("queue", "Queue length", "#8b5cf6", value: 7);
    }

    [Test]
    public async Task A_provider_line_shows_the_providers_own_values_when_nothing_is_configured()
    {
        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.That(snapshot, Has.Exactly(1).Matches<LiveMetricReading>(r => r.Alias == "queue"));
        LiveMetricReading queue = snapshot.Single(r => r.Alias == "queue");
        Assert.That(queue.Name, Is.EqualTo("Queue length"));
        Assert.That(queue.Color, Is.EqualTo("#8b5cf6"));
        Assert.That(queue.Value, Is.EqualTo(7));
    }

    [Test]
    public async Task Name_colour_and_sort_order_under_the_providers_alias_replace_its_own()
    {
        _options.Channels["web"] = new LiveViewChannelOptions { SortOrder = 100 };
        _options.Channels["queue"] = new LiveViewChannelOptions { Name = "Queued work", Color = "#000", SortOrder = 5 };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.That(snapshot.Select(r => r.Alias).Where(a => a is "queue" or "web"), Is.EqualTo(["queue", "web"]));
        Assert.That(snapshot, Has.Exactly(1).Matches<LiveMetricReading>(r => r.Alias == "queue"));
        LiveMetricReading queue = snapshot.Single(r => r.Alias == "queue");
        Assert.That(queue.Name, Is.EqualTo("Queued work"));
        Assert.That(queue.Color, Is.EqualTo("#000"));
        Assert.That(queue.Value, Is.EqualTo(7));
    }

    [Test]
    public async Task Values_left_out_keep_the_providers_own()
    {
        _options.Channels["web"] = new LiveViewChannelOptions { SortOrder = 100 };
        _options.Channels["queue"] = new LiveViewChannelOptions { Color = "#000" };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.That(snapshot.Select(r => r.Alias).Where(a => a is "queue" or "web"), Is.EqualTo(["web", "queue"]));
        Assert.That(snapshot, Has.Exactly(1).Matches<LiveMetricReading>(r => r.Alias == "queue"));
        LiveMetricReading queue = snapshot.Single(r => r.Alias == "queue");
        Assert.That(queue.Name, Is.EqualTo("Queue length"));
        Assert.That(queue.Color, Is.EqualTo("#000"));
    }

    [Test]
    public async Task A_switched_off_provider_line_is_dropped_and_the_provider_is_not_called()
    {
        _options.Channels["queue"] = new LiveViewChannelOptions { Enabled = false };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.That(snapshot, Has.None.Matches<LiveMetricReading>(r => r.Alias == "queue"));
        Assert.That(_queue.Calls, Is.Zero);
    }

    [Test]
    public async Task Settings_under_a_providers_alias_add_no_channel_line()
    {
        _options.Channels["QUEUE"] = new LiveViewChannelOptions { Name = "Queued work", PathPrefixes = ["/"] };

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot();

        Assert.That(snapshot, Has.Exactly(1).Matches<LiveMetricReading>(r => r.Alias.Equals("queue", StringComparison.OrdinalIgnoreCase)));
        LiveMetricReading line = snapshot.Single(r => r.Alias.Equals("queue", StringComparison.OrdinalIgnoreCase));
        Assert.That(line.Alias, Is.EqualTo("queue"));
        Assert.That(line.Name, Is.EqualTo("Queued work"));
    }

    [Test]
    public async Task A_provider_aliased_like_a_built_in_channel_replaces_it()
    {
        var api = new StubMetricProvider("api", "Queue depth", value: 4);
        new LiveViewOptionsDefaults([]).PostConfigure(null, _options);

        await AssertReplacedByProvider(api, "/api/products");
    }

    [Test]
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

        Assert.That(classifier.Classify(context.Request).Sessions.Select(s => s.Channel), Does.Not.Contain(provider.Alias));

        IReadOnlyList<LiveMetricReading> snapshot = await Snapshot(provider);

        Assert.That(snapshot, Has.Exactly(1).Matches<LiveMetricReading>(r => r.Alias == provider.Alias));
        LiveMetricReading line = snapshot.Single(r => r.Alias == provider.Alias);
        Assert.That(line.Value, Is.EqualTo(4));
        Assert.That(provider.Calls, Is.EqualTo(1));
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
