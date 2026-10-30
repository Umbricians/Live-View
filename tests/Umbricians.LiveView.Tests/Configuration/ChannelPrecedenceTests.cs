using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Extensions;

namespace Umbricians.LiveView.Tests.Configuration;

/// <summary>
/// Channel values come from appsettings first, then <c>AddLiveViewChannel</c>, then the built-in defaults, whichever
/// order the package and the site register in.
/// </summary>
public class ChannelPrecedenceTests
{
    private const string Members = Constants.Channels.Members;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Code_beats_built_in_defaults(bool packageFirst)
    {
        LiveViewOptions options = Build(packageFirst, appsettings: [], builder =>
            builder.AddLiveViewChannel(Members, "Signed-in members", c => c.Color = "#000"));

        LiveViewChannelOptions members = options.Channels[Members];
        Assert.Equal("Signed-in members", members.Name);
        Assert.Equal("#000", members.Color);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Appsettings_beat_code_and_built_in_defaults(bool packageFirst)
    {
        LiveViewOptions options = Build(
            packageFirst,
            appsettings: new()
            {
                [$"{Constants.ConfigurationSection}:Channels:{Members}:Name"] = "From appsettings",
                [$"{Constants.ConfigurationSection}:Channels:{Members}:SortOrder"] = "5",
            },
            builder => builder.AddLiveViewChannel(Members, "From code", c => c.SortOrder = 7));

        LiveViewChannelOptions members = options.Channels[Members];
        Assert.Equal("From appsettings", members.Name);
        Assert.Equal(5, members.SortOrder);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Built_in_defaults_fill_what_is_left(bool packageFirst)
    {
        LiveViewOptions options = Build(
            packageFirst,
            appsettings: new() { [$"{Constants.ConfigurationSection}:Channels:{Members}:SortOrder"] = "5" },
            builder => builder.AddLiveViewChannel(Members, "Signed-in members", c => c.Color = "#000"));

        LiveViewChannelOptions members = options.Channels[Members];
        LiveViewChannelOptions builtIn = LiveViewOptionsDefaults.BuiltInChannels[Members];
        Assert.Equal(builtIn.PathPrefixes, members.PathPrefixes);
        Assert.Equal(builtIn.SessionKeys, members.SessionKeys);
        Assert.Equal(builtIn.Exclusive, members.Exclusive);
        Assert.Equal(builtIn.IgnoreStaticFiles, members.IgnoreStaticFiles);
        Assert.Equal(
            LiveViewOptionsDefaults.BuiltInChannels[Constants.Channels.Web].Name,
            options.Channels[Constants.Channels.Web].Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_channel_declared_in_code_is_added(bool packageFirst)
    {
        LiveViewOptions options = Build(packageFirst, appsettings: [], builder =>
            builder.AddLiveViewChannel("signalr", "SignalR clients", c => c.Color = "#0ea5e9"));

        LiveViewChannelOptions signalr = options.Channels["signalr"];
        Assert.Equal("SignalR clients", signalr.Name);
        Assert.Equal("#0ea5e9", signalr.Color);
        Assert.Null(signalr.PathPrefixes);
    }

    [Fact]
    public void The_first_code_declaration_of_a_channel_wins()
    {
        LiveViewOptions options = Build(packageFirst: true, appsettings: [], builder =>
        {
            builder.AddLiveViewChannel("signalr", "First", c => c.Color = "#111");
            builder.AddLiveViewChannel("signalr", "Second", c => c.SortOrder = 3);
        });

        LiveViewChannelOptions signalr = options.Channels["signalr"];
        Assert.Equal("First", signalr.Name);
        Assert.Equal("#111", signalr.Color);
        Assert.Equal(3, signalr.SortOrder);
    }

    /// <summary>
    /// Registers options the way <see cref="Composers.LiveViewComposer"/> does, before or after the site's own
    /// registrations, and resolves the result.
    /// </summary>
    private static LiveViewOptions Build(
        bool packageFirst,
        Dictionary<string, string?> appsettings,
        Action<IUmbracoBuilder> site)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(appsettings).Build();
        ServiceCollection services = new();
        IUmbracoBuilder builder = ServicesOnlyBuilder.Create(services);

        void Package()
        {
            services.AddOptions<LiveViewOptions>().Bind(config.GetSection(Constants.ConfigurationSection));
            LiveViewOptionsDefaults.AddTo(services);
        }

        if (packageFirst)
        {
            Package();
            site(builder);
        }
        else
        {
            site(builder);
            Package();
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<LiveViewOptions>>().Value;
    }

    /// <summary>An <see cref="IUmbracoBuilder"/> that only exposes <see cref="IUmbracoBuilder.Services"/>.</summary>
    public class ServicesOnlyBuilder : DispatchProxy
    {
        private IServiceCollection _services = null!;

        public static IUmbracoBuilder Create(IServiceCollection services)
        {
            IUmbracoBuilder builder = Create<IUmbracoBuilder, ServicesOnlyBuilder>();
            ((ServicesOnlyBuilder)(object)builder)._services = services;
            return builder;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == $"get_{nameof(IUmbracoBuilder.Services)}"
                ? _services
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
