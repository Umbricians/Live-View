using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Tests.Configuration;

/// <summary>
/// A channel's settings as they apply: what it was given, with the gaps filled the same way wherever they are read.
/// </summary>
public class ChannelSettingsTests
{
    private LiveViewOptions _options;

    [SetUp]
    public void SetUp() => _options = new LiveViewOptions { ActivityWindowSeconds = 300 };

    [Test]
    public void A_channel_given_only_paths_gets_the_defaults_for_the_rest()
    {
        _options.Channels["shop"] = new LiveViewChannelOptions { PathPrefixes = ["/shop"] };

        ChannelSettings shop = ChannelSettings.Of(_options, "shop");

        Assert.That(shop.Name, Is.EqualTo("shop"));
        Assert.That(shop.Color, Is.Null);
        Assert.That(shop.SortOrder, Is.Zero);
        Assert.That(shop.Enabled, Is.True);
        Assert.That(shop.PathPrefixes, Is.EqualTo(["/shop"]));
        Assert.That(shop.ExcludePathPrefixes, Is.Empty);
        Assert.That(shop.SessionKeys, Is.EqualTo([Constants.SessionKeySources.Client]));
        Assert.That(shop.ActivityWindow, Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(shop.IgnoreStaticFiles, Is.False);
        Assert.That(shop.Exclusive, Is.True);
    }

    [Test]
    public void A_channel_window_beats_the_site_wide_window()
    {
        _options.Channels["api"] = new LiveViewChannelOptions { ActivityWindowSeconds = 60 };

        Assert.That(ChannelSettings.Of(_options, "api").ActivityWindow, Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void A_channel_with_no_settings_uses_the_site_wide_window()
    {
        _options.ActivityWindowSeconds = 120;

        Assert.That(ChannelSettings.Of(_options, "unknown").ActivityWindow, Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    [Test]
    public void Channels_come_in_sort_order()
    {
        _options.Channels["late"] = new LiveViewChannelOptions { SortOrder = 50 };
        _options.Channels["unsorted"] = new LiveViewChannelOptions();
        _options.Channels["early"] = new LiveViewChannelOptions { SortOrder = 10 };

        Assert.That(ChannelSettings.InOrder(_options, []).Select(c => c.Alias), Is.EqualTo(["unsorted", "early", "late"]));
    }

    [Test]
    public void Settings_under_a_metric_providers_alias_are_not_a_channel()
    {
        _options.Channels["queue"] = new LiveViewChannelOptions { Name = "Queued work" };
        _options.Channels["web"] = new LiveViewChannelOptions();

        Assert.That(ChannelSettings.InOrder(_options, [new StubMetricProvider("queue")]).Select(c => c.Alias), Is.EqualTo(["web"]));
    }
}
