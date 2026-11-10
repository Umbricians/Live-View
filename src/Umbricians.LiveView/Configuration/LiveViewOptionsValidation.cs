using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbricians.LiveView.Configuration;

/// <summary>
/// Fills the dashboard defaults and replaces values that can't work with their defaults, logging a warning for
/// each, so a mistake in appsettings never breaks the dashboard.
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
