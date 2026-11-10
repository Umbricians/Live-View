using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Storage;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Composers;

/// <summary>What the composer registers, through <c>AddLiveView</c> on a plain service collection.</summary>
public class ServiceCollectionExtensionsTests
{
    [Test]
    public void Binds_the_options_from_the_Live_View_section()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{Constants.ConfigurationSection}:ActivityWindowSeconds"] = "120" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveView(config);

        LiveViewOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<LiveViewOptions>>().Value;

        Assert.That(options.ActivityWindowSeconds, Is.EqualTo(120));
    }

    [Test]
    public void Lets_MVC_find_the_internal_controllers()
    {
        ServiceCollection services = AddLiveView();

        var parts = (ApplicationPartManager)services.Last(d => d.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance!;

        Assert.That(parts.FeatureProviders, Has.Some.InstanceOf<LiveViewControllerFeatureProvider>());
    }

    [Test]
    public void Adds_the_tracking_middleware_through_a_pipeline_filter_named_after_the_package()
    {
        ServiceCollection services = AddLiveView();

        UmbracoPipelineOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<UmbracoPipelineOptions>>().Value;

        Assert.That(options.PipelineFilters, Has.Some.Matches<IUmbracoPipelineFilter>(filter => filter.Name == "Umbricians.LiveView"));
    }

    [Test]
    public void Uses_the_system_clock_unless_the_site_registers_its_own()
    {
        var siteClock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(siteClock);
        services.AddLiveView(new ConfigurationBuilder().Build());

        Assert.That(Resolved(AddLiveView(), typeof(TimeProvider)).ImplementationInstance, Is.SameAs(TimeProvider.System));
        Assert.That(Resolved(services, typeof(TimeProvider)).ImplementationInstance, Is.SameAs(siteClock));
    }

    public static IEnumerable<TestCaseData> Replaceable =>
    [
        new(typeof(ILiveViewActivityStore), typeof(InMemoryActivityStore), typeof(SharedActivityStore)),
        new(typeof(IBackofficeMessageStore), typeof(InMemoryBackofficeMessageStore), typeof(SharedBackofficeMessageStore)),
        new(typeof(IMemberAvatarProvider), typeof(PropertyMemberAvatarProvider), typeof(SiteAvatarProvider)),
        new(typeof(IMemberMessageInbox), typeof(MemberMessageInbox), typeof(SiteInbox)),
        new(typeof(ILiveActivityTracker), typeof(LiveActivityTracker), typeof(SiteTracker)),
    ];

    public static IEnumerable<Type> Services =>
    [
        typeof(RequestClassifier),
        typeof(UnforwardedProxyDetector),
        typeof(LiveActivityMiddleware),
        typeof(LiveMetricsService),
        typeof(IBackofficeUserDirectory),
        typeof(IMemberDirectory),
        typeof(BackofficePresence),
        typeof(MemberPresence),
        typeof(McpCallers),
        typeof(ILiveViewPresence),
        typeof(BackofficeMessenger),
        typeof(MemberMessagingService),
        typeof(ILiveViewActivityStore),
        typeof(IBackofficeMessageStore),
        typeof(ILiveActivityTracker),
        typeof(IMemberAvatarProvider),
        typeof(IMemberMessageInbox),
    ];

    // A registration that went missing, or a constructor that gained a dependency nothing registers, would
    // otherwise only fail on the site's first tracked request or dashboard load.
    [TestCaseSource(nameof(Services))]
    public void Every_service_resolves_with_only_Umbracos_services_added(Type service)
    {
        using ServiceProvider provider = WithUmbracoServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.That(provider.GetService(service), Is.Not.Null);
    }

    [Test]
    public void Live_View_presence_is_the_MCP_callers_service()
    {
        using ServiceProvider provider = WithUmbracoServices().BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ILiveViewPresence>(), Is.SameAs(provider.GetRequiredService<McpCallers>()));
    }

    [Test]
    public void The_directories_read_from_Umbraco()
    {
        using ServiceProvider provider = WithUmbracoServices().BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IBackofficeUserDirectory>(), Is.TypeOf<UmbracoBackofficeUserDirectory>());
        Assert.That(provider.GetRequiredService<IMemberDirectory>(), Is.TypeOf<UmbracoMemberDirectory>());
    }

    /// <summary>
    /// <c>AddLiveView</c> plus stand-ins for the Umbraco services Live View takes. The constructors only keep them,
    /// so the stand-ins throw if anything is called on them.
    /// </summary>
    private static ServiceCollection WithUmbracoServices()
    {
        ServiceCollection services = AddLiveView();
        services.AddLogging();
        services.AddSingleton<IServerEventRouter>(new NullServerEventRouter());
        services.AddSingleton<IEventAggregator>(new RecordingEventAggregator());
        services.AddSingleton(AppCaches.NoCache);
        services.AddSingleton((MediaFileManager)RuntimeHelpers.GetUninitializedObject(typeof(MediaFileManager)));
        services.AddSingleton(NotCalled<IUserService>());
        services.AddSingleton(NotCalled<IUserGroupService>());
        services.AddSingleton(NotCalled<IUserConnectionManager>());
        services.AddSingleton(NotCalled<IImageUrlGenerator>());
        services.AddSingleton(NotCalled<IIdKeyMap>());
        services.AddSingleton(NotCalled<IMemberService>());
        services.AddSingleton(NotCalled<IPublishedMemberCache>());
        services.AddSingleton(NotCalled<IPublishedUrlProvider>());
        return services;
    }

    private static T NotCalled<T>() where T : class => DispatchProxy.Create<T, NotCalledProxy>();

    public class NotCalledProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} was called on a stand-in.");
    }

    [TestCaseSource(nameof(Replaceable))]
    public void A_sites_own_implementation_wins_whichever_order_it_is_registered_in(Type service, Type ours, Type sites)
    {
        var before = new ServiceCollection();
        before.AddSingleton(service, sites);
        before.AddLiveView(new ConfigurationBuilder().Build());

        ServiceCollection after = AddLiveView();
        after.AddSingleton(service, sites);

        Assert.That(Resolved(AddLiveView(), service).ImplementationType, Is.EqualTo(ours));
        Assert.That(Resolved(before, service).ImplementationType, Is.EqualTo(sites));
        Assert.That(Resolved(after, service).ImplementationType, Is.EqualTo(sites));
    }

    private static ServiceCollection AddLiveView()
    {
        var services = new ServiceCollection();
        services.AddLiveView(new ConfigurationBuilder().Build());
        return services;
    }

    /// <summary>The registration the container resolves for a service: the last one added.</summary>
    private static ServiceDescriptor Resolved(IServiceCollection services, Type service) =>
        services.Last(d => d.ServiceType == service);

    private sealed class SiteAvatarProvider : IMemberAvatarProvider
    {
        public Task<string?> GetAvatarUrlAsync(Umbraco.Cms.Core.Models.IMember member, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SiteTracker : ILiveActivityTracker
    {
        public Task TouchAsync(string channel, string sessionKey) => Task.CompletedTask;

        public Task<IAsyncDisposable> BeginActivityAsync(string channel, string sessionKey) => throw new NotSupportedException();

        public Task EndAsync(string channel, string sessionKey) => Task.CompletedTask;

        public Task<int> CountActiveAsync(string channel) => Task.FromResult(0);
    }

    private sealed class SiteInbox : IMemberMessageInbox
    {
        public void Add(MemberMessage message)
        {
        }

        public IReadOnlyList<MemberMessage> GetUnread(Guid memberKey) => [];

        public void MarkRead(Guid memberKey, Guid messageKey)
        {
        }
    }
}
