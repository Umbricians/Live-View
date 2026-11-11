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
    private RecordingLogger<LiveViewOptionsValidation> _logger;

    [SetUp]
    public void SetUp() => _logger = new RecordingLogger<LiveViewOptionsValidation>();

    [Test]
    public void With_no_settings_the_defaults_apply()
    {
        LiveViewOptions options = Build();

        Assert.That(options.BackofficeMessageLifetimeMinutes, Is.EqualTo(15));
        Assert.That(options.Dashboard.RefreshIntervals, Is.EqualTo([2, 5, 10, 15, 30]));
        Assert.That(options.Dashboard.DefaultRefreshInterval, Is.EqualTo(5));
        Assert.That(options.Dashboard.MaxReadings, Is.EqualTo(120));
        Assert.That(options.BotUserAgents, Is.Empty);
        Assert.That(_logger.Warnings, Is.Empty);
    }

    [Test]
    public void Configured_refresh_intervals_replace_the_built_in_ones()
    {
        LiveViewOptions options = Build(Intervals(10, 30, 60));

        Assert.That(options.Dashboard.RefreshIntervals, Is.EqualTo([10, 30, 60]));
        Assert.That(options.Dashboard.DefaultRefreshInterval, Is.EqualTo(10));
        Assert.That(_logger.Warnings, Is.Empty);
    }

    [Test]
    public void Refresh_intervals_are_put_in_order_once_each()
    {
        LiveViewOptions options = Build(Intervals(30, 10, 30));

        Assert.That(options.Dashboard.RefreshIntervals, Is.EqualTo([10, 30]));
    }

    [Test]
    public void A_default_interval_that_is_not_offered_falls_back_with_a_warning()
    {
        var settings = Intervals(2, 5, 10);
        settings["Dashboard:DefaultRefreshInterval"] = "7";

        LiveViewOptions options = Build(settings);

        Assert.That(options.Dashboard.DefaultRefreshInterval, Is.EqualTo(5));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("Dashboard:DefaultRefreshInterval")));
    }

    [TestCase("BackofficeMessageLifetimeMinutes", "0")]
    [TestCase("Dashboard:MaxReadings", "-1")]
    [TestCase("Dashboard:RefreshIntervals:0", "0")]
    public void Values_that_are_not_positive_fall_back_to_their_defaults_with_a_warning(string setting, string value)
    {
        LiveViewOptions options = Build(new() { [setting] = value });

        Assert.That(options.BackofficeMessageLifetimeMinutes, Is.EqualTo(15));
        Assert.That(options.Dashboard.MaxReadings, Is.EqualTo(120));
        Assert.That(options.Dashboard.RefreshIntervals, Is.EqualTo([2, 5, 10, 15, 30]));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain(setting.Replace(":0", ""))));
        Assert.That(_logger.Warnings, Has.Exactly(1).Items);
    }

    // Either would leave the dashboard empty: no session would count as active, or none would be stored.
    [TestCase("ActivityWindowSeconds", "0")]
    [TestCase("MaxSessionsPerChannel", "-5")]
    public void Tracking_limits_that_are_not_positive_fall_back_to_their_defaults_with_a_warning(string setting, string value)
    {
        LiveViewOptions options = Build(new() { [setting] = value });

        Assert.That(options.ActivityWindowSeconds, Is.EqualTo(300));
        Assert.That(options.MaxSessionsPerChannel, Is.EqualTo(100_000));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain(setting)));
        Assert.That(_logger.Warnings, Has.Exactly(1).Items);
    }

    [Test]
    public void A_built_in_channels_window_that_is_not_positive_falls_back_to_its_built_in_one()
    {
        LiveViewOptions options = Build(new() { ["Channels:api:ActivityWindowSeconds"] = "0" });

        Assert.That(options.Channels["api"].ActivityWindowSeconds, Is.EqualTo(60));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("Channels:api:ActivityWindowSeconds")));
    }

    [Test]
    public void A_channel_declared_in_code_falls_back_to_the_window_code_gave_it()
    {
        LiveViewOptions options = Build(
            new() { ["Channels:shop:ActivityWindowSeconds"] = "0" },
            builder => builder.AddLiveViewChannel("shop", "Shop", c => c.ActivityWindowSeconds = 120));

        Assert.That(options.Channels["shop"].ActivityWindowSeconds, Is.EqualTo(120));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("Channels:shop:ActivityWindowSeconds").And.Contain("120")));
    }

    // Code takes precedence over the built-in defaults, so restyling the api channel in code moves its fallback too.
    [Test]
    public void A_built_in_channel_given_a_window_in_code_falls_back_to_that_window()
    {
        LiveViewOptions options = Build(
            new() { ["Channels:api:ActivityWindowSeconds"] = "-1" },
            builder => builder.AddLiveViewChannel("api", "API calls", c => c.ActivityWindowSeconds = 90));

        Assert.That(options.Channels["api"].ActivityWindowSeconds, Is.EqualTo(90));
    }

    [Test]
    public void A_window_of_zero_from_code_falls_back_past_it()
    {
        LiveViewOptions options = Build(
            code: builder => builder.AddLiveViewChannel("api", "API calls", c => c.ActivityWindowSeconds = 0));

        Assert.That(options.Channels["api"].ActivityWindowSeconds, Is.EqualTo(60));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("Channels:api:ActivityWindowSeconds")));
    }

    [Test]
    public void Any_other_channels_window_that_is_not_positive_falls_back_to_the_site_wide_one()
    {
        LiveViewOptions options = Build(new()
        {
            ["ActivityWindowSeconds"] = "120",
            ["Channels:shop:PathPrefixes:0"] = "/shop",
            ["Channels:shop:ActivityWindowSeconds"] = "-1",
        });

        Assert.That(options.Channels["shop"].ActivityWindowSeconds, Is.Null);
        Assert.That(ChannelSettings.Of(options, "shop").ActivityWindow, Is.EqualTo(TimeSpan.FromSeconds(120)));
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("Channels:shop:ActivityWindowSeconds").And.Contain("120")));
    }

    [Test]
    public void A_time_zone_the_server_does_not_know_is_ignored_with_a_warning()
    {
        LiveViewOptions options = Build(new() { ["MessageTimeZone"] = "Mars/Olympus_Mons" });

        Assert.That(options.MessageTimeZone, Is.Null);
        Assert.That(_logger.Warnings, Has.Exactly(1).Matches(Does.Contain("MessageTimeZone")));
    }

    [Test]
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

        Assert.That(options.BackofficeMessageLifetimeMinutes, Is.EqualTo(60));
        Assert.That(options.Dashboard.RefreshIntervals, Is.EqualTo([5, 10]));
        Assert.That(options.Dashboard.DefaultRefreshInterval, Is.EqualTo(10));
        Assert.That(options.Dashboard.MaxReadings, Is.EqualTo(300));
        Assert.That(options.ActivityWindowSeconds, Is.EqualTo(120));
        Assert.That(options.MaxSessionsPerChannel, Is.EqualTo(500));
        Assert.That(options.Channels["api"].ActivityWindowSeconds, Is.EqualTo(30));
        Assert.That(options.MessageTimeZone, Is.EqualTo("America/Denver"));
        Assert.That(_logger.Warnings, Is.Empty);
    }

    [Test]
    public void Bot_user_agents_from_appsettings_are_bound()
    {
        LiveViewOptions options = Build(new() { ["BotUserAgents:0"] = "StatusCake" });

        Assert.That(options.BotUserAgents, Is.EqualTo(["StatusCake"]));
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
