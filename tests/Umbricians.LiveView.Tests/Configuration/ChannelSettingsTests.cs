using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Tests.Configuration;

/// <summary>
/// A channel's settings as they apply: what it was given, with the gaps filled the same way wherever they are read.
/// </summary>
public class ChannelSettingsTests
{
    private readonly LiveViewOptions _options = new() { ActivityWindowSeconds = 300 };

    [Fact]
    public void A_channel_given_only_paths_gets_the_defaults_for_the_rest()
    {
        _options.Channels["shop"] = new LiveViewChannelOptions { PathPrefixes = ["/shop"] };

        ChannelSettings shop = ChannelSettings.Of(_options, "shop");

        Assert.Equal("shop", shop.Name);
        Assert.Null(shop.Color);
        Assert.Equal(0, shop.SortOrder);
        Assert.True(shop.Enabled);
        Assert.Equal(["/shop"], shop.PathPrefixes);
        Assert.Empty(shop.ExcludePathPrefixes);
        Assert.Equal([Constants.SessionKeySources.Client], shop.SessionKeys);
        Assert.Equal(TimeSpan.FromMinutes(5), shop.ActivityWindow);
        Assert.False(shop.IgnoreStaticFiles);
        Assert.True(shop.Exclusive);
    }

    [Fact]
    public void A_channel_window_beats_the_site_wide_window()
    {
        _options.Channels["api"] = new LiveViewChannelOptions { ActivityWindowSeconds = 60 };

        Assert.Equal(TimeSpan.FromMinutes(1), ChannelSettings.Of(_options, "api").ActivityWindow);
    }

    [Fact]
    public void A_channel_with_no_settings_uses_the_site_wide_window()
    {
        _options.ActivityWindowSeconds = 120;

        Assert.Equal(TimeSpan.FromMinutes(2), ChannelSettings.Of(_options, "unknown").ActivityWindow);
    }

    [Fact]
    public void Channels_come_in_sort_order()
    {
        _options.Channels["late"] = new LiveViewChannelOptions { SortOrder = 50 };
        _options.Channels["unsorted"] = new LiveViewChannelOptions();
        _options.Channels["early"] = new LiveViewChannelOptions { SortOrder = 10 };

        Assert.Equal(["unsorted", "early", "late"], ChannelSettings.InOrder(_options).Select(c => c.Alias));
    }
}
