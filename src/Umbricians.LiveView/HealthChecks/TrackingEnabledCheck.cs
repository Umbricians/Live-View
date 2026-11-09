using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;

namespace Umbricians.LiveView.HealthChecks;

/// <summary>Warns when request counting is turned off.</summary>
[HealthCheck(
    "a3c97e20-51d4-4b8a-8e6f-0f2b9d47c6a3",
    "Request counting",
    Description = "Whether Live View counts requests to the site (TrackingEnabled).",
    Group = LiveViewHealthChecks.Group)]
internal sealed class TrackingEnabledCheck : HealthCheck
{
    private readonly IOptionsMonitor<LiveViewOptions> _options;

    public TrackingEnabledCheck(IOptionsMonitor<LiveViewOptions> options) => _options = options;

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        HealthCheckStatus status = _options.CurrentValue.TrackingEnabled
            ? new HealthCheckStatus("Request counting is on.") { ResultType = StatusResultType.Success }
            : new HealthCheckStatus(
                "Request counting is off (<code>Umbricians:LiveView:TrackingEnabled</code> is <code>false</code>), so requests to the site aren't counted on the Live View chart. "
                + "Set it to <code>true</code>, or remove it, to count them again.")
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = LiveViewHealthChecks.GettingStartedLink,
            };

        return Task.FromResult<IEnumerable<HealthCheckStatus>>([status]);
    }
}
