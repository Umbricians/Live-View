using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Extensions;

namespace Umbricians.LiveView.Tests.Configuration;

/// <summary>Live View's settings: what appsettings can say, and what is used when it says something that can't work.</summary>
public class LiveViewOptionsValidationTests
{
    private readonly RecordingLogger<LiveViewOptionsValidation> _logger = new();

    [Fact]
    public void With_no_settings_the_defaults_apply()
    {
        LiveViewOptions options = Build();

        Assert.Equal(15, options.BackofficeMessageLifetimeMinutes);
        Assert.Equal([2, 5, 10, 15, 30], options.Dashboard.RefreshIntervals);
        Assert.Equal(5, options.Dashboard.DefaultRefreshInterval);
        Assert.Equal(120, options.Dashboard.MaxReadings);
        Assert.Empty(options.BotUserAgents);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void Configured_refresh_intervals_replace_the_built_in_ones()
    {
        LiveViewOptions options = Build(Intervals(10, 30, 60));

        Assert.Equal([10, 30, 60], options.Dashboard.RefreshIntervals);
        Assert.Equal(10, options.Dashboard.DefaultRefreshInterval);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void Refresh_intervals_are_put_in_order_once_each()
    {
        LiveViewOptions options = Build(Intervals(30, 10, 30));

        Assert.Equal([10, 30], options.Dashboard.RefreshIntervals);
    }

    [Fact]
    public void A_default_interval_that_is_not_offered_falls_back_with_a_warning()
    {
        var settings = Intervals(2, 5, 10);
        settings["Dashboard:DefaultRefreshInterval"] = "7";

        LiveViewOptions options = Build(settings);

        Assert.Equal(5, options.Dashboard.DefaultRefreshInterval);
        Assert.Single(_logger.Warnings, w => w.Contains("Dashboard:DefaultRefreshInterval"));
    }

    [Theory]
    [InlineData("BackofficeMessageLifetimeMinutes", "0")]
    [InlineData("Dashboard:MaxReadings", "-1")]
    [InlineData("Dashboard:RefreshIntervals:0", "0")]
    public void Values_that_are_not_positive_fall_back_to_their_defaults_with_a_warning(string setting, string value)
    {
        LiveViewOptions options = Build(new() { [setting] = value });

        Assert.Equal(15, options.BackofficeMessageLifetimeMinutes);
        Assert.Equal(120, options.Dashboard.MaxReadings);
        Assert.Equal([2, 5, 10, 15, 30], options.Dashboard.RefreshIntervals);
        Assert.Single(_logger.Warnings, w => w.Contains(setting.Replace(":0", "")));
        Assert.Single(_logger.Warnings);
    }

    // Either would leave the dashboard empty: no session would count as active, or none would be stored.
    [Theory]
    [InlineData("ActivityWindowSeconds", "0")]
    [InlineData("MaxSessionsPerChannel", "-5")]
    public void Tracking_limits_that_are_not_positive_fall_back_to_their_defaults_with_a_warning(string setting, string value)
    {
        LiveViewOptions options = Build(new() { [setting] = value });

        Assert.Equal(300, options.ActivityWindowSeconds);
        Assert.Equal(100_000, options.MaxSessionsPerChannel);
        Assert.Single(_logger.Warnings, w => w.Contains(setting));
        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public void A_built_in_channels_window_that_is_not_positive_falls_back_to_its_built_in_one()
    {
        LiveViewOptions options = Build(new() { ["Channels:api:ActivityWindowSeconds"] = "0" });

        Assert.Equal(60, options.Channels["api"].ActivityWindowSeconds);
        Assert.Single(_logger.Warnings, w => w.Contains("Channels:api:ActivityWindowSeconds"));
    }

    [Fact]
    public void A_channel_declared_in_code_falls_back_to_the_window_code_gave_it()
    {
        LiveViewOptions options = Build(
            new() { ["Channels:shop:ActivityWindowSeconds"] = "0" },
            builder => builder.AddLiveViewChannel("shop", "Shop", c => c.ActivityWindowSeconds = 120));

        Assert.Equal(120, options.Channels["shop"].ActivityWindowSeconds);
        Assert.Single(_logger.Warnings, w => w.Contains("Channels:shop:ActivityWindowSeconds") && w.Contains("120"));
    }

    // Code takes precedence over the built-in defaults, so restyling the api channel in code moves its fallback too.
    [Fact]
    public void A_built_in_channel_given_a_window_in_code_falls_back_to_that_window()
    {
        LiveViewOptions options = Build(
            new() { ["Channels:api:ActivityWindowSeconds"] = "-1" },
            builder => builder.AddLiveViewChannel("api", "API calls", c => c.ActivityWindowSeconds = 90));

        Assert.Equal(90, options.Channels["api"].ActivityWindowSeconds);
    }

    [Fact]
    public void A_window_of_zero_from_code_falls_back_past_it()
    {
        LiveViewOptions options = Build(
            code: builder => builder.AddLiveViewChannel("api", "API calls", c => c.ActivityWindowSeconds = 0));

        Assert.Equal(60, options.Channels["api"].ActivityWindowSeconds);
        Assert.Single(_logger.Warnings, w => w.Contains("Channels:api:ActivityWindowSeconds"));
    }

    [Fact]
    public void Any_other_channels_window_that_is_not_positive_falls_back_to_the_site_wide_one()
    {
        LiveViewOptions options = Build(new()
        {
            ["ActivityWindowSeconds"] = "120",
            ["Channels:shop:PathPrefixes:0"] = "/shop",
            ["Channels:shop:ActivityWindowSeconds"] = "-1",
        });

        Assert.Null(options.Channels["shop"].ActivityWindowSeconds);
        Assert.Equal(TimeSpan.FromSeconds(120), ChannelSettings.Of(options, "shop").ActivityWindow);
        Assert.Single(_logger.Warnings, w => w.Contains("Channels:shop:ActivityWindowSeconds") && w.Contains("120"));
    }

    [Fact]
    public void A_time_zone_the_server_does_not_know_is_ignored_with_a_warning()
    {
        LiveViewOptions options = Build(new() { ["MessageTimeZone"] = "Mars/Olympus_Mons" });

        Assert.Null(options.MessageTimeZone);
        Assert.Single(_logger.Warnings, w => w.Contains("MessageTimeZone"));
    }

    [Fact]
    public void Valid_settings_are_kept()
    {
        LiveViewOptions options = Build(new(Intervals(5, 10))
        {
            ["BackofficeMessageLifetimeMinutes"] = "60",
            ["Dashboard:DefaultRefreshInterval"] = "10",
            ["Dashboard:MaxReadings"] = "300",
            ["ActivityWindowSeconds"] = "120",
            ["MaxSessionsPerChannel"] = "500",
            ["Channels:api:ActivityWindowSeconds"] = "30",
            ["MessageTimeZone"] = "America/Denver",
        });

        Assert.Equal(60, options.BackofficeMessageLifetimeMinutes);
        Assert.Equal([5, 10], options.Dashboard.RefreshIntervals);
        Assert.Equal(10, options.Dashboard.DefaultRefreshInterval);
        Assert.Equal(300, options.Dashboard.MaxReadings);
        Assert.Equal(120, options.ActivityWindowSeconds);
        Assert.Equal(500, options.MaxSessionsPerChannel);
        Assert.Equal(30, options.Channels["api"].ActivityWindowSeconds);
        Assert.Equal("America/Denver", options.MessageTimeZone);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public void Bot_user_agents_from_appsettings_are_bound()
    {
        LiveViewOptions options = Build(new() { ["BotUserAgents:0"] = "StatusCake" });

        Assert.Equal(["StatusCake"], options.BotUserAgents);
    }

    private static Dictionary<string, string?> Intervals(params int[] seconds) =>
        seconds.Select((s, i) => ($"Dashboard:RefreshIntervals:{i}", (string?)s.ToString()))
            .ToDictionary(p => p.Item1, p => p.Item2);

    /// <summary>Builds the options through the composer's own registration, from settings relative to the Live View section.</summary>
    private LiveViewOptions Build(Dictionary<string, string?>? settings = null, Action<IUmbracoBuilder>? code = null)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection((settings ?? []).ToDictionary(p => $"{Constants.ConfigurationSection}:{p.Key}", p => p.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LiveViewOptionsValidation>>(_logger);
        services.AddLiveView(config);
        code?.Invoke(ChannelPrecedenceTests.ServicesOnlyBuilder.Create(services));

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<LiveViewOptions>>().Value;
    }
}
