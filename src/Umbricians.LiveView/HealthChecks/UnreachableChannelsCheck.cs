using System.Net;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.HealthChecks;

/// <summary>Warns about channels that never count a request because an earlier exclusive channel counts their paths first.</summary>
[HealthCheck(
    "2f6c8e95-7b14-4d3a-9c50-e1a8d4b6f272",
    "Channel paths",
    Description = "Every channel with paths should be able to count requests, rather than having all its paths counted first by an earlier exclusive channel.",
    Group = LiveViewHealthChecks.Group)]
internal sealed class UnreachableChannelsCheck : HealthCheck
{
    private readonly RequestClassifier _classifier;

    public UnreachableChannelsCheck(RequestClassifier classifier) => _classifier = classifier;

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        List<HealthCheckStatus> statuses = _classifier.UnreachableChannels()
            .Select(u => new HealthCheckStatus(
                $"The <strong>{WebUtility.HtmlEncode(u.Channel.Name)}</strong> channel (<code>{WebUtility.HtmlEncode(u.Channel.Alias)}</code>) never counts a request: "
                + $"all its paths are counted first by {string.Join(", ", u.CountedFirstBy.Select(Describe))}, earlier exclusive channel(s). "
                + $"Give it a <code>SortOrder</code> below {u.CountedFirstBy.Min(c => c.SortOrder)}, or change its <code>PathPrefixes</code>.")
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = LiveViewHealthChecks.GettingStartedLink,
            })
            .ToList();

        if (statuses.Count == 0)
        {
            statuses.Add(new HealthCheckStatus("Every channel with paths can count requests.")
            {
                ResultType = StatusResultType.Success,
            });
        }

        return Task.FromResult<IEnumerable<HealthCheckStatus>>(statuses);
    }

    private static string Describe(ChannelSettings channel) =>
        $"<strong>{WebUtility.HtmlEncode(channel.Name)}</strong> (<code>{WebUtility.HtmlEncode(channel.Alias)}</code>)";
}
