using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Configuration;

/// <summary>
/// Fills the dashboard defaults and replaces any value that can't work with its default, logging a warning for
/// each, so a mistake in appsettings never stops the site or empties the dashboard. Failing at startup was considered
/// and rejected: a dashboard setting shouldn't be able to take a whole site down (#38).
/// </summary>
internal sealed class LiveViewOptionsValidation(ILogger<LiveViewOptionsValidation> logger)
    : IPostConfigureOptions<LiveViewOptions>
{
    private const string Section = Constants.ConfigurationSection;

    /// <summary>Registers this post-configure, once however often it is called.</summary>
    internal static void AddTo(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<LiveViewOptions>, LiveViewOptionsValidation>());

    public void PostConfigure(string? name, LiveViewOptions options)
    {
        LiveViewOptions defaults = new();

        if (options.ActivityWindowSeconds <= 0)
        {
            WarnNotPositive($"{Section}:ActivityWindowSeconds", options.ActivityWindowSeconds, defaults.ActivityWindowSeconds);
            options.ActivityWindowSeconds = defaults.ActivityWindowSeconds;
        }

        // Runs after the channel defaults are filled in, so a channel with no window of its own already has its
        // built-in one; only a configured window can be wrong.
        foreach ((string alias, LiveViewChannelOptions channel) in options.Channels)
        {
            if (channel.ActivityWindowSeconds <= 0)
            {
                int? fallback = LiveViewOptionsDefaults.BuiltInChannels.TryGetValue(alias, out LiveViewChannelOptions? builtIn)
                    ? builtIn.ActivityWindowSeconds
                    : null;
                WarnNotPositive(
                    $"{Section}:Channels:{alias}:ActivityWindowSeconds",
                    channel.ActivityWindowSeconds,
                    fallback ?? options.ActivityWindowSeconds);
                channel.ActivityWindowSeconds = fallback;
            }
        }

        if (options.MaxSessionsPerChannel <= 0)
        {
            WarnNotPositive($"{Section}:MaxSessionsPerChannel", options.MaxSessionsPerChannel, defaults.MaxSessionsPerChannel);
            options.MaxSessionsPerChannel = defaults.MaxSessionsPerChannel;
        }

        if (!string.IsNullOrWhiteSpace(options.MessageTimeZone)
            && MemberMessageExpiry.TimeZoneId(options.MessageTimeZone) is null)
        {
            logger.LogWarning(
                "Live View setting {Setting} is {Value}, which is not a time zone this server knows; using each sender's own time zone instead.",
                $"{Section}:MessageTimeZone", options.MessageTimeZone);
            options.MessageTimeZone = null;
        }

        if (options.BackofficeMessageLifetimeMinutes <= 0)
        {
            WarnNotPositive(
                $"{Section}:BackofficeMessageLifetimeMinutes",
                options.BackofficeMessageLifetimeMinutes,
                LiveViewOptions.DefaultBackofficeMessageLifetimeMinutes);
            options.BackofficeMessageLifetimeMinutes = LiveViewOptions.DefaultBackofficeMessageLifetimeMinutes;
        }

        LiveViewDashboardOptions dashboard = options.Dashboard;

        // Configured intervals are added to a list that already holds values, so the list starts empty.
        if (dashboard.RefreshIntervals.Length == 0)
        {
            dashboard.RefreshIntervals = [.. LiveViewDashboardOptions.DefaultRefreshIntervals];
        }
        else if (dashboard.RefreshIntervals.Any(i => i <= 0))
        {
            WarnNotPositive(
                $"{Section}:Dashboard:RefreshIntervals",
                string.Join(", ", dashboard.RefreshIntervals),
                string.Join(", ", LiveViewDashboardOptions.DefaultRefreshIntervals));
            dashboard.RefreshIntervals = [.. LiveViewDashboardOptions.DefaultRefreshIntervals];
        }
        else
        {
            dashboard.RefreshIntervals = [.. dashboard.RefreshIntervals.Distinct().Order()];
        }

        if (dashboard.DefaultRefreshInterval is { } chosen && !dashboard.RefreshIntervals.Contains(chosen))
        {
            logger.LogWarning(
                "Live View setting {Setting} is {Value}, which is not one of the RefreshIntervals; using {Default} instead.",
                $"{Section}:Dashboard:DefaultRefreshInterval", chosen, FirstChoice(dashboard));
            dashboard.DefaultRefreshInterval = null;
        }

        dashboard.DefaultRefreshInterval ??= FirstChoice(dashboard);

        if (dashboard.MaxReadings <= 0)
        {
            WarnNotPositive(
                $"{Section}:Dashboard:MaxReadings", dashboard.MaxReadings, LiveViewDashboardOptions.DefaultMaxReadings);
            dashboard.MaxReadings = LiveViewDashboardOptions.DefaultMaxReadings;
        }
    }

    private static int FirstChoice(LiveViewDashboardOptions dashboard) =>
        dashboard.RefreshIntervals.Contains(LiveViewDashboardOptions.PreferredRefreshInterval)
            ? LiveViewDashboardOptions.PreferredRefreshInterval
            : dashboard.RefreshIntervals[0];

    private void WarnNotPositive(string setting, object value, object fallback) =>
        logger.LogWarning(
            "Live View setting {Setting} is {Value}, which is not a positive number; using {Default} instead.",
            setting, value, fallback);
}
