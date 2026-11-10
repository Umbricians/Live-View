using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.HealthChecks;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>What the Channel paths check reports for built-in and site channels.</summary>
public class UnreachableChannelsCheckTests
{
    private LiveViewOptions _options;

    [SetUp]
    public void SetUp()
    {
        _options = new LiveViewOptions();
        new LiveViewOptionsDefaults([]).PostConfigure(null, _options);
    }

    [Test]
    public async Task The_built_in_channels_are_all_reachable()
    {
        IEnumerable<HealthCheckStatus> statuses = await Run();
        Assert.That(statuses, Has.Exactly(1).Items);
        HealthCheckStatus status = statuses.Single();

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Success));
    }

    [Test]
    public async Task A_channel_under_the_backoffice_paths_after_it_is_a_warning()
    {
        _options.Channels["shadow"] = new LiveViewChannelOptions { PathPrefixes = ["/umbraco/shadow"], SortOrder = 60 };

        IEnumerable<HealthCheckStatus> statuses = await Run();
        Assert.That(statuses, Has.Exactly(1).Items);
        HealthCheckStatus status = statuses.Single();

        Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Warning));
        Assert.That(status.Message, Does.Contain("shadow"));
        Assert.That(status.Message, Does.Contain("backoffice"));
        Assert.That(status.Message, Does.Contain("below 50"));
    }

    [Test]
    public async Task Channel_names_are_html_encoded()
    {
        _options.Channels["shadow"] = new LiveViewChannelOptions
        {
            Name = "<i>Shadow</i>",
            PathPrefixes = ["/umbraco/shadow"],
            SortOrder = 60,
        };

        IEnumerable<HealthCheckStatus> statuses = await Run();
        Assert.That(statuses, Has.Exactly(1).Items);
        HealthCheckStatus status = statuses.Single();

        Assert.That(status.Message, Does.Contain("&lt;i&gt;Shadow"));
        Assert.That(status.Message, Does.Not.Contain("<i>"));
    }

    private async Task<IEnumerable<HealthCheckStatus>> Run() =>
        await new UnreachableChannelsCheck(new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options), [], [])).GetStatusAsync();
}
