using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.Authorization;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Security;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Controllers;

/// <summary>The dashboard-settings endpoint: what it returns for the registered options, and who may call it.</summary>
public class DashboardSettingsTests
{
    [Test]
    public void With_no_settings_the_endpoint_returns_the_defaults()
    {
        DashboardSettingsResponseModel settings = Controller([]).DashboardSettings();

        Assert.That(settings.RefreshIntervals, Is.EqualTo([2, 5, 10, 15, 30]));
        Assert.That(settings.DefaultRefreshInterval, Is.EqualTo(5));
        Assert.That(settings.MaxReadings, Is.EqualTo(120));
    }

    [Test]
    public void A_default_that_is_not_configured_is_filled_in_before_the_endpoint_passes_it_on()
    {
        DashboardSettingsResponseModel settings = Controller(new()
        {
            ["Dashboard:RefreshIntervals:0"] = "10",
            ["Dashboard:RefreshIntervals:1"] = "30",
        }).DashboardSettings();

        Assert.That(settings.RefreshIntervals, Is.EqualTo([10, 30]));
        Assert.That(settings.DefaultRefreshInterval, Is.EqualTo(10));
    }

    [Test]
    public void Dashboard_settings_needs_the_same_authorization_as_snapshot()
    {
        Type controller = typeof(DashboardController);

        Assert.That(Describe(controller.GetMethod(nameof(DashboardController.DashboardSettings))!), Is.EqualTo(Describe(controller.GetMethod(nameof(DashboardController.Snapshot))!)));

        Assert.That(controller.GetCustomAttributes<AuthorizeAttribute>(), Has.Some.Matches<AuthorizeAttribute>(a => a?.Policy == AuthorizationPolicies.BackOfficeAccess));
        Assert.That(controller.GetCustomAttributes<LiveViewPermissionAttribute>(), Has.Some.Matches<LiveViewPermissionAttribute>(a => a?.Arguments?.SequenceEqual([Constants.Permissions.View]) == true));
    }

    private static List<string> Describe(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Where(a => a is IAuthorizeData or IAllowAnonymous or LiveViewPermissionAttribute)
            .Select(a => a switch
            {
                IAuthorizeData data => $"{a.GetType().FullName}|{data.Policy}",
                LiveViewPermissionAttribute permission => $"{a.GetType().FullName}|{string.Join(",", permission.Arguments!)}",
                _ => a.GetType().FullName!,
            })
            .Order()
            .ToList();

    /// <summary>The controller over the options the composer registers for the given Live View settings.</summary>
    private static DashboardController Controller(Dictionary<string, string?> settings)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(p => $"{Constants.ConfigurationSection}:{p.Key}", p => p.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveView(config);
        IOptionsMonitor<LiveViewOptions> options = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<LiveViewOptions>>();

        var store = new InMemoryActivityStore(options);
        var users = new InMemoryBackofficeUserDirectory();
        var backoffice = new BackofficePresence(store, users, options, TimeProvider.System);
        var members = new MemberPresence(store, new InMemoryMemberDirectory(), options, TimeProvider.System);
        var tracker = new LiveActivityTracker(store, options, TimeProvider.System);
        return new DashboardController(
            new LiveMetricsService(tracker, backoffice, members, [], options, NullLogger<LiveMetricsService>.Instance),
            backoffice,
            members,
            new BackofficeMessenger(
                new NullServerEventRouter(),
                backoffice,
                users,
                new InMemoryBackofficeMessageStore(TimeProvider.System),
                new RecordingEventAggregator(),
                options,
                TimeProvider.System),
            new NoBackOfficeSecurityAccessor(),
            options);
    }
}
