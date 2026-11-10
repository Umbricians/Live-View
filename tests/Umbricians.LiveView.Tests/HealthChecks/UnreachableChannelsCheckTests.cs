using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.HealthChecks;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Channel paths check reports for built-in and site channels.</summary>
public class UnreachableChannelsCheckTests
{
    private readonly LiveViewOptions _options = new();

    public UnreachableChannelsCheckTests() => new LiveViewOptionsDefaults([]).PostConfigure(null, _options);

    [Fact]
    public async Task The_built_in_channels_are_all_reachable()
    {
        HealthCheckStatus status = Assert.Single(await Run());

        Assert.Equal(StatusResultType.Success, status.ResultType);
    }

    [Fact]
    public async Task A_channel_under_the_backoffice_paths_after_it_is_a_warning()
    {
        _options.Channels["shadow"] = new LiveViewChannelOptions { PathPrefixes = ["/umbraco/shadow"], SortOrder = 60 };

        HealthCheckStatus status = Assert.Single(await Run());

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains("shadow", status.Message);
        Assert.Contains("backoffice", status.Message);
        Assert.Contains("below 50", status.Message);
    }

    [Fact]
    public async Task Channel_names_are_html_encoded()
    {
        _options.Channels["shadow"] = new LiveViewChannelOptions
        {
            Name = "<i>Shadow</i>",
            PathPrefixes = ["/umbraco/shadow"],
            SortOrder = 60,
        };

        HealthCheckStatus status = Assert.Single(await Run());

        Assert.Contains("&lt;i&gt;Shadow", status.Message);
        Assert.DoesNotContain("<i>", status.Message);
    }

    private async Task<IEnumerable<HealthCheckStatus>> Run() =>
        await new UnreachableChannelsCheck(new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options), [], [])).GetStatusAsync();
}
