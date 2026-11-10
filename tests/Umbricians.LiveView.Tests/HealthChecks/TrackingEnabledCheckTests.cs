using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.HealthChecks;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>Whether a site that has switched request counting off is told.</summary>
public class TrackingEnabledCheckTests
{
    [Test]
    public async Task Counting_on_is_a_success()
    {
        HealthCheckStatus status = await Run(new LiveViewOptions { TrackingEnabled = true });

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
    }

    [Test]
    public async Task Counting_off_is_a_warning_naming_the_setting()
    {
        HealthCheckStatus status = await Run(new LiveViewOptions { TrackingEnabled = false });

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
        Assert.That(status.Message, Does.Contain("TrackingEnabled"));
    }

    private static async Task<HealthCheckStatus> Run(LiveViewOptions options)
    {
        IEnumerable<HealthCheckStatus> statuses = await new TrackingEnabledCheck(new StaticOptionsMonitor<LiveViewOptions>(options)).GetStatusAsync();
        Assert.That(statuses, Has.Exactly(1).Items);
        return statuses.Single();
    }
}
