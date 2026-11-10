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
    [Fact]
    public void With_no_settings_the_endpoint_returns_the_defaults()
    {
        DashboardSettingsResponseModel settings = Controller([]).DashboardSettings();

        Assert.Equal([2, 5, 10, 15, 30], settings.RefreshIntervals);
        Assert.Equal(5, settings.DefaultRefreshInterval);
        Assert.Equal(120, settings.MaxReadings);
    }

    [Fact]
    public void A_default_that_is_not_configured_is_filled_in_before_the_endpoint_passes_it_on()
    {
        DashboardSettingsResponseModel settings = Controller(new()
        {
            ["Dashboard:RefreshIntervals:0"] = "10",
            ["Dashboard:RefreshIntervals:1"] = "30",
        }).DashboardSettings();

        Assert.Equal([10, 30], settings.RefreshIntervals);
        Assert.Equal(10, settings.DefaultRefreshInterval);
    }

    [Fact]
    public void Dashboard_settings_needs_the_same_authorization_as_snapshot()
    {
        Type controller = typeof(LiveViewApiController);

        Assert.Equal(Describe(controller.GetMethod(nameof(LiveViewApiController.Snapshot))!),
            Describe(controller.GetMethod(nameof(LiveViewApiController.DashboardSettings))!));

        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(),
            a => a.Policy == AuthorizationPolicies.BackOfficeAccess);
        Assert.Contains(controller.GetCustomAttributes<LiveViewPermissionAttribute>(),
            a => a.Arguments!.SequenceEqual([Constants.Permissions.View]));
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
    private static LiveViewApiController Controller(Dictionary<string, string?> settings)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(p => $"{Constants.ConfigurationSection}:{p.Key}", p => p.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        LiveViewComposer.AddOptions(services, config);
        IOptionsMonitor<LiveViewOptions> options = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<LiveViewOptions>>();

        var store = new InMemoryActivityStore(options);
        var users = new InMemoryBackofficeUserDirectory();
        var backoffice = new BackofficePresence(store, users, options, TimeProvider.System);
        var members = new MemberPresence(store, new InMemoryMemberDirectory(), options, TimeProvider.System);
        var tracker = new LiveActivityTracker(store, options, TimeProvider.System);
        return new LiveViewApiController(
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
