using System.Net;
using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>Which channels a request counts in, and what it says about who was seen.</summary>
public class RequestClassifierTests
{
    private readonly LiveViewOptions _options = new();
    private readonly RequestClassifier _classifier;

    public RequestClassifierTests() => _classifier = new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options));

    [Theory]
    [InlineData("/api", true)]
    [InlineData("/api/products", true)]
    [InlineData("/API/Products", true)]
    [InlineData("/apix", false)]
    [InlineData("/", false)]
    public void Prefixes_match_whole_path_segments(string path, bool counted)
    {
        Channel("api", prefixes: ["/api/"]);

        Assert.Equal(counted ? ["api"] : [], Channels(Get(path)));
    }

    [Fact]
    public void An_excluded_prefix_is_not_counted()
    {
        Channel("web", prefixes: ["/"], exclude: ["/umbraco"]);

        Assert.Empty(Channels(Get("/umbraco/section")));
        Assert.Equal(["web"], Channels(Get("/about")));
    }

    [Fact]
    public void Channels_are_offered_in_sort_order_up_to_the_first_exclusive_one()
    {
        Channel("web", prefixes: ["/"], sortOrder: 30);
        Channel("shared", prefixes: ["/"], sortOrder: 10, exclusive: false);
        Channel("first", prefixes: ["/"], sortOrder: 20);

        Assert.Equal(["shared", "first"], Channels(Get("/page")));
    }

    [Fact]
    public void A_disabled_channel_is_skipped()
    {
        Channel("off", prefixes: ["/"], sortOrder: 10).Enabled = false;
        Channel("web", prefixes: ["/"], sortOrder: 20);

        Assert.Equal(["web"], Channels(Get("/page")));
    }

    [Fact]
    public void A_static_file_is_left_out_of_channels_that_ignore_them_but_still_stops_there()
    {
        Channel("pages", prefixes: ["/"], sortOrder: 10).IgnoreStaticFiles = true;
        Channel("later", prefixes: ["/"], sortOrder: 20);
        Channel("assets", prefixes: ["/assets"], sortOrder: 5).IgnoreStaticFiles = false;

        Assert.Empty(Channels(Get("/site.CSS")));
        Assert.Equal(["assets"], Channels(Get("/assets/site.css")));
    }

    [Theory]
    [InlineData("Googlebot/2.1")]
    [InlineData("Mozilla/5.0 (compatible; UptimeMonitor)")]
    public void Bots_are_not_counted(string userAgent)
    {
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Headers.UserAgent = userAgent;

        Assert.False(_classifier.Classify(context.Request).IsTracked);
    }

    [Fact]
    public void Bots_are_counted_when_the_site_asks_for_them()
    {
        _options.IgnoreBots = false;
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Headers.UserAgent = "Googlebot/2.1";

        Assert.Equal(["web"], Channels(context));
    }

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    public void Preflight_and_head_requests_are_not_counted(string method)
    {
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Method = method;

        Assert.False(_classifier.Classify(context.Request).IsTracked);
    }

    [Fact]
    public void Nothing_is_counted_while_tracking_is_off()
    {
        _options.TrackingEnabled = false;
        Channel("web", prefixes: ["/"]);

        Assert.False(_classifier.Classify(Get("/page").Request).IsTracked);
    }

    [Fact]
    public void A_channel_without_a_session_key_is_not_counted()
    {
        Channel("api", prefixes: ["/"], keys: ["Header:Api-Key", "Unknown"]);

        RequestClassification request = _classifier.Classify(Get("/page").Request);

        Assert.Empty(request.Sessions);
        Assert.False(request.IsTracked);
    }

    [Fact]
    public void A_later_source_is_used_when_an_earlier_one_is_empty()
    {
        Channel("api", prefixes: ["/"], keys: ["Header:Api-Key", "Client"]);
        DefaultHttpContext context = Get("/page");
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal(["Client:203.0.113.7|"], _classifier.Classify(context.Request).Sessions.Select(s => s.SessionKey));
    }

    [Fact]
    public void Member_and_backoffice_requests_say_who_was_seen_rather_than_opening_sessions()
    {
        Channel(Constants.Channels.Backoffice, prefixes: ["/umbraco"], sortOrder: 10, keys: ["User"]);
        Channel(Constants.Channels.Members, prefixes: ["/"], sortOrder: 20, keys: ["User"], exclusive: false);
        Channel("web", prefixes: ["/"], sortOrder: 30);

        RequestClassification backoffice = _classifier.Classify(Get("/umbraco/section").Request);
        RequestClassification page = _classifier.Classify(Get("/page").Request);

        Assert.True(backoffice.BackofficeSeen);
        Assert.False(backoffice.MemberSeen);
        Assert.Empty(backoffice.Sessions);
        Assert.True(page.MemberSeen);
        Assert.False(page.BackofficeSeen);
        Assert.Equal(["web"], page.Sessions.Select(s => s.Channel));
    }

    [Fact]
    public void An_mcp_request_is_marked_as_one_and_still_counts_its_session()
    {
        Channel(Constants.Channels.Mcp, prefixes: ["/mcp"], keys: ["Header:Mcp-Session-Id"]);
        DefaultHttpContext context = Get("/mcp");
        context.Request.Headers["Mcp-Session-Id"] = "s1";

        RequestClassification request = _classifier.Classify(context.Request);

        Assert.True(request.McpCall);
        Assert.Equal([new ChannelSession(Constants.Channels.Mcp, "Header:Mcp-Session-Id:s1")], request.Sessions);
    }

    private LiveViewChannelOptions Channel(
        string alias,
        string[] prefixes,
        int sortOrder = 0,
        string[]? exclude = null,
        string[]? keys = null,
        bool? exclusive = null)
    {
        var channel = new LiveViewChannelOptions
        {
            PathPrefixes = prefixes,
            ExcludePathPrefixes = exclude,
            SortOrder = sortOrder,
            SessionKeys = keys,
            Exclusive = exclusive,
        };
        _options.Channels[alias] = channel;
        return channel;
    }

    private IEnumerable<string> Channels(DefaultHttpContext context) =>
        _classifier.Classify(context.Request).Sessions.Select(s => s.Channel);

    private static DefaultHttpContext Get(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        return context;
    }
}
