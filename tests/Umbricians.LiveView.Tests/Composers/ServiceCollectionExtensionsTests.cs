using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Storage;

namespace Umbricians.LiveView.Tests.Composers;

/// <summary>What the composer registers, through <c>AddLiveView</c> on a plain service collection.</summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Binds_the_options_from_the_Live_View_section()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{Constants.ConfigurationSection}:ActivityWindowSeconds"] = "120" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveView(config);

        LiveViewOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<LiveViewOptions>>().Value;

        Assert.Equal(120, options.ActivityWindowSeconds);
    }

    [Fact]
    public void Lets_MVC_find_the_internal_controllers()
    {
        ServiceCollection services = AddLiveView();

        var parts = (ApplicationPartManager)services.Last(d => d.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance!;

        Assert.Contains(parts.FeatureProviders, provider => provider is LiveViewControllerFeatureProvider);
    }

    [Fact]
    public void Adds_the_tracking_middleware_through_a_pipeline_filter_named_after_the_package()
    {
        ServiceCollection services = AddLiveView();

        UmbracoPipelineOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<UmbracoPipelineOptions>>().Value;

        Assert.Contains(options.PipelineFilters, filter => filter.Name == "Umbricians.LiveView");
    }

    [Fact]
    public void Uses_the_system_clock_unless_the_site_registers_its_own()
    {
        var siteClock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(siteClock);
        services.AddLiveView(new ConfigurationBuilder().Build());

        Assert.Same(TimeProvider.System, Resolved(AddLiveView(), typeof(TimeProvider)).ImplementationInstance);
        Assert.Same(siteClock, Resolved(services, typeof(TimeProvider)).ImplementationInstance);
    }

    public static TheoryData<Type, Type, Type> Replaceable => new()
    {
        { typeof(ILiveViewActivityStore), typeof(InMemoryActivityStore), typeof(SharedActivityStore) },
        { typeof(IBackofficeMessageStore), typeof(InMemoryBackofficeMessageStore), typeof(SharedBackofficeMessageStore) },
        { typeof(IMemberAvatarProvider), typeof(PropertyMemberAvatarProvider), typeof(SiteAvatarProvider) },
        { typeof(IMemberMessageInbox), typeof(MemberMessageInbox), typeof(SiteInbox) },
    };

    [Theory]
    [MemberData(nameof(Replaceable))]
    public void A_sites_own_implementation_wins_whichever_order_it_is_registered_in(Type service, Type ours, Type sites)
    {
        var before = new ServiceCollection();
        before.AddSingleton(service, sites);
        before.AddLiveView(new ConfigurationBuilder().Build());

        ServiceCollection after = AddLiveView();
        after.AddSingleton(service, sites);

        Assert.Equal(ours, Resolved(AddLiveView(), service).ImplementationType);
        Assert.Equal(sites, Resolved(before, service).ImplementationType);
        Assert.Equal(sites, Resolved(after, service).ImplementationType);
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
