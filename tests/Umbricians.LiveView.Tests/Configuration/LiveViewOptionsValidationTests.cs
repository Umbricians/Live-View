using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.Tests.Configuration;

/// <summary>The dashboard and lifetime settings: what appsettings can say, and what is used when it says something that can't work.</summary>
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

    [Fact]
    public void Valid_settings_are_kept()
    {
        LiveViewOptions options = Build(new(Intervals(5, 10))
        {
            ["BackofficeMessageLifetimeMinutes"] = "60",
            ["Dashboard:DefaultRefreshInterval"] = "10",
            ["Dashboard:MaxReadings"] = "300",
        });

        Assert.Equal(60, options.BackofficeMessageLifetimeMinutes);
        Assert.Equal([5, 10], options.Dashboard.RefreshIntervals);
        Assert.Equal(10, options.Dashboard.DefaultRefreshInterval);
        Assert.Equal(300, options.Dashboard.MaxReadings);
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
    private LiveViewOptions Build(Dictionary<string, string?>? settings = null)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection((settings ?? []).ToDictionary(p => $"{Constants.ConfigurationSection}:{p.Key}", p => p.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LiveViewOptionsValidation>>(_logger);
        LiveViewComposer.AddOptions(services, config);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<LiveViewOptions>>().Value;
    }
}
