using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>Whether a site that has switched request counting off is told.</summary>
public class TrackingEnabledCheckTests
{
    [Fact]
    public async Task Counting_on_is_a_success()
    {
        HealthCheckStatus status = await Run(new LiveViewOptions { TrackingEnabled = true });

        Assert.Equal(StatusResultType.Success, status.ResultType);
    }

    [Fact]
    public async Task Counting_off_is_a_warning_naming_the_setting()
    {
        HealthCheckStatus status = await Run(new LiveViewOptions { TrackingEnabled = false });

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains("TrackingEnabled", status.Message);
    }

    private static async Task<HealthCheckStatus> Run(LiveViewOptions options) =>
        Assert.Single(await new TrackingEnabledCheck(new StaticOptionsMonitor<LiveViewOptions>(options)).GetStatusAsync());
}
