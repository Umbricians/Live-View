using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>Which channels a request counts in, and what it says about who was seen.</summary>
public class RequestClassifierTests
{
    private LiveViewOptions _options;
    private RequestClassifier _classifier;

    [SetUp]
    public void SetUp()
    {
        _options = new LiveViewOptions();
        _classifier = new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options), [], []);
    }

    [TestCase("/api", true)]
    [TestCase("/api/products", true)]
    [TestCase("/API/Products", true)]
    [TestCase("/apix", false)]
    [TestCase("/", false)]
    public void Prefixes_match_whole_path_segments(string path, bool counted)
    {
        Channel("api", prefixes: ["/api/"]);

        IEnumerable<string> expected = counted ? ["api"] : [];

        Assert.That(Channels(Get(path)), Is.EqualTo(expected));
    }

    [Test]
    public void An_excluded_prefix_is_not_counted()
    {
        Channel("web", prefixes: ["/"], exclude: ["/umbraco"]);

        Assert.That(Channels(Get("/umbraco/section")), Is.Empty);
        Assert.That(Channels(Get("/about")), Is.EqualTo(["web"]));
    }

    [Test]
    public void Channels_are_offered_in_sort_order_up_to_the_first_exclusive_one()
    {
        Channel("web", prefixes: ["/"], sortOrder: 30);
        Channel("shared", prefixes: ["/"], sortOrder: 10, exclusive: false);
        Channel("first", prefixes: ["/"], sortOrder: 20);

        Assert.That(Channels(Get("/page")), Is.EqualTo(["shared", "first"]));
    }

    [Test]
    public void A_disabled_channel_is_skipped()
    {
        Channel("off", prefixes: ["/"], sortOrder: 10).Enabled = false;
        Channel("web", prefixes: ["/"], sortOrder: 20);

        Assert.That(Channels(Get("/page")), Is.EqualTo(["web"]));
    }

    [Test]
    public void A_static_file_is_left_out_of_channels_that_ignore_them_but_still_stops_there()
    {
        Channel("pages", prefixes: ["/"], sortOrder: 10).IgnoreStaticFiles = true;
        Channel("later", prefixes: ["/"], sortOrder: 20);
        Channel("assets", prefixes: ["/assets"], sortOrder: 5).IgnoreStaticFiles = false;

        Assert.That(Channels(Get("/site.CSS")), Is.Empty);
        Assert.That(Channels(Get("/assets/site.css")), Is.EqualTo(["assets"]));
    }

    [Test]
    public void Path_settings_under_a_metric_providers_alias_are_ignored()
    {
        Channel("queue", prefixes: ["/"], sortOrder: 10);
        Channel("web", prefixes: ["/"], sortOrder: 20);
        var classifier = new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options), [], [new StubMetricProvider("queue")]);

        Assert.That(classifier.Classify(Get("/page").Request).Sessions.Select(s => s.Channel), Is.EqualTo(["web"]));
    }

    // The README lists the built-in crawler patterns, so sites know a headless test browser won't show.
    [Test]
    public void The_readme_lists_exactly_the_built_in_crawler_patterns()
    {
        string readme = File.ReadAllText(RepositoryFile("README.md"));
        Match paragraph = Regex.Match(readme, @"`IgnoreBots` \(default true\) leaves out every request whose User-Agent contains (?<list>.+?), ignoring case", RegexOptions.Singleline);
        Assert.That(paragraph.Success, Is.True, "The README's IgnoreBots paragraph wasn't found.");

        string[] documented = Regex.Matches(paragraph.Groups["list"].Value, "`([^`]+)`").Select(m => m.Groups[1].Value).Order().ToArray();
        string[] builtIn = RequestClassifier.BotUserAgent().ToString().Split('|').Order().ToArray();

        Assert.That(documented, Is.EqualTo(builtIn));
    }

    [TestCase("Googlebot/2.1")]
    [TestCase("Mozilla/5.0 (compatible; UptimeMonitor)")]
    // Playwright's and Puppeteer's headless Chromium, as end-to-end tests send it, and Lighthouse.
    [TestCase("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/140.0.0.0 Safari/537.36")]
    [TestCase("Mozilla/5.0 (Linux; Android 11; moto g power (2022)) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Mobile Safari/537.36 Chrome-Lighthouse")]
    public void Bots_are_not_counted(string userAgent)
    {
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Headers.UserAgent = userAgent;

        Assert.That(_classifier.Classify(context.Request).IsTracked, Is.False);
    }

    [Test]
    public void Bots_are_counted_when_the_site_asks_for_them()
    {
        _options.IgnoreBots = false;
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Headers.UserAgent = "Googlebot/2.1";

        Assert.That(Channels(context), Is.EqualTo(["web"]));
    }

    [Test]
    public void A_site_can_add_its_own_crawler_patterns()
    {
        _options.BotUserAgents = ["StatusCake"];
        Channel("web", prefixes: ["/"]);

        Assert.That(_classifier.Classify(WithUserAgent("Mozilla/5.0 (compatible; statuscake)").Request).IsTracked, Is.False);
    }

    [Test]
    public void Built_in_crawler_patterns_still_apply_alongside_the_sites_own()
    {
        _options.BotUserAgents = ["StatusCake"];
        Channel("web", prefixes: ["/"]);

        Assert.That(_classifier.Classify(WithUserAgent("Googlebot/2.1").Request).IsTracked, Is.False);
    }

    [Test]
    public void A_browser_matching_no_pattern_is_counted()
    {
        _options.BotUserAgents = ["StatusCake"];
        Channel("web", prefixes: ["/"]);

        Assert.That(Channels(WithUserAgent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/130.0")), Is.EqualTo(["web"]));
    }

    [Test]
    public void Blank_crawler_patterns_match_nothing()
    {
        _options.BotUserAgents = ["", "  "];
        Channel("web", prefixes: ["/"]);

        Assert.That(Channels(WithUserAgent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/130.0")), Is.EqualTo(["web"]));
    }

    [Test]
    public void The_sites_crawler_patterns_are_ignored_while_bots_are_counted()
    {
        _options.IgnoreBots = false;
        _options.BotUserAgents = ["StatusCake"];
        Channel("web", prefixes: ["/"]);

        Assert.That(Channels(WithUserAgent("StatusCake")), Is.EqualTo(["web"]));
    }

    [TestCase("OPTIONS")]
    [TestCase("HEAD")]
    public void Preflight_and_head_requests_are_not_counted(string method)
    {
        Channel("web", prefixes: ["/"]);
        DefaultHttpContext context = Get("/page");
        context.Request.Method = method;

        Assert.That(_classifier.Classify(context.Request).IsTracked, Is.False);
    }

    [Test]
    public void Nothing_is_counted_while_tracking_is_off()
    {
        _options.TrackingEnabled = false;
        Channel("web", prefixes: ["/"]);

        Assert.That(_classifier.Classify(Get("/page").Request).IsTracked, Is.False);
    }

    [Test]
    public void A_channel_without_a_session_key_is_not_counted()
    {
        Channel("api", prefixes: ["/"], keys: ["Header:Api-Key", "Unknown"]);

        RequestClassification request = _classifier.Classify(Get("/page").Request);

        Assert.That(request.Sessions, Is.Empty);
        Assert.That(request.IsTracked, Is.False);
    }

    [Test]
    public void A_later_source_is_used_when_an_earlier_one_is_empty()
    {
        Channel("api", prefixes: ["/"], keys: ["Header:Api-Key", "Client"]);
        DefaultHttpContext context = Get("/page");
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.That(_classifier.Classify(context.Request).Sessions.Select(s => s.SessionKey), Is.EqualTo(["Client:203.0.113.7|"]));
    }

    [Test]
    public void Member_and_backoffice_requests_say_who_was_seen_rather_than_opening_sessions()
    {
        Channel(Constants.Channels.Backoffice, prefixes: ["/umbraco"], sortOrder: 10, keys: ["User"]);
        Channel(Constants.Channels.Members, prefixes: ["/"], sortOrder: 20, keys: ["User"], exclusive: false);
        Channel("web", prefixes: ["/"], sortOrder: 30);

        RequestClassification backoffice = _classifier.Classify(Get("/umbraco/section").Request);
        RequestClassification page = _classifier.Classify(Get("/page").Request);

        Assert.That(backoffice.BackofficeSeen, Is.True);
        Assert.That(backoffice.MemberSeen, Is.False);
        Assert.That(backoffice.Sessions, Is.Empty);
        Assert.That(page.MemberSeen, Is.True);
        Assert.That(page.BackofficeSeen, Is.False);
        Assert.That(page.Sessions.Select(s => s.Channel), Is.EqualTo(["web"]));
    }

    [Test]
    public void An_mcp_request_is_marked_as_one_and_still_counts_its_session()
    {
        Channel(Constants.Channels.Mcp, prefixes: ["/mcp"], keys: ["Header:Mcp-Session-Id"]);
        DefaultHttpContext context = Get("/mcp");
        context.Request.Headers["Mcp-Session-Id"] = "s1";

        RequestClassification request = _classifier.Classify(context.Request);

        Assert.That(request.McpCall, Is.True);
        Assert.That(request.Sessions, Is.EqualTo([new ChannelSession(Constants.Channels.Mcp, "Header:Mcp-Session-Id:s1")]));
    }

    [Test]
    public void A_channel_whose_paths_an_earlier_exclusive_channel_counts_is_unreachable()
    {
        Channel("first", prefixes: ["/"], sortOrder: 10);
        Channel("later", prefixes: ["/later"], sortOrder: 20);

        IReadOnlyList<UnreachableChannel> unreachableChannels = _classifier.UnreachableChannels();
        Assert.That(unreachableChannels, Has.Exactly(1).Items);
        UnreachableChannel unreachable = unreachableChannels.Single();

        Assert.That(unreachable.Channel.Alias, Is.EqualTo("later"));
        Assert.That(unreachable.CountedFirstBy.Select(c => c.Alias), Is.EqualTo(["first"]));
    }

    [Test]
    public void A_channel_with_a_lower_sort_order_is_reachable()
    {
        Channel("first", prefixes: ["/"], sortOrder: 20);
        Channel("later", prefixes: ["/later"], sortOrder: 10);

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void A_non_exclusive_or_disabled_earlier_channel_leaves_later_ones_reachable()
    {
        Channel("shared", prefixes: ["/"], sortOrder: 10, exclusive: false);
        Channel("off", prefixes: ["/"], sortOrder: 20).Enabled = false;
        Channel("later", prefixes: ["/later"], sortOrder: 30);

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void An_exclusion_in_the_earlier_channel_leaves_paths_it_lets_through_reachable()
    {
        Channel("first", prefixes: ["/"], sortOrder: 10, exclude: ["/umbraco"]);
        Channel("inside", prefixes: ["/umbraco/x"], sortOrder: 20);
        Channel("everything", prefixes: ["/"], sortOrder: 30);

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void A_channel_fed_from_code_is_never_unreachable()
    {
        Channel("first", prefixes: ["/"], sortOrder: 10);
        Channel("code", prefixes: [], sortOrder: 20);

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void An_unreachable_channels_paths_count_in_the_earlier_channel()
    {
        Channel("first", prefixes: ["/"], sortOrder: 10);
        Channel("later", prefixes: ["/later"], sortOrder: 20);

        Assert.That(_classifier.UnreachableChannels().Select(u => u.Channel.Alias), Is.EqualTo(["later"]));
        Assert.That(Channels(Get("/later/page")), Is.EqualTo(["first"]));
    }

    [Test]
    public void A_later_channel_limited_to_hosts_or_a_predicate_is_not_reported_as_unreachable()
    {
        Channel("first", prefixes: ["/"], sortOrder: 10);
        Channel("brand-b", prefixes: ["/shop"], sortOrder: 20).Hosts = ["brand-b.com"];
        Channel("checkout", prefixes: ["/cart"], sortOrder: 30).RequestPredicate = _ => true;

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void An_earlier_exclusive_channel_limited_to_hosts_or_a_predicate_does_not_cover_later_paths()
    {
        Channel("brand-a", prefixes: ["/"], sortOrder: 10).Hosts = ["brand-a.com"];
        Channel("picky", prefixes: ["/"], sortOrder: 20).RequestPredicate = _ => false;
        Channel("later", prefixes: ["/later"], sortOrder: 30);

        Assert.That(_classifier.UnreachableChannels(), Is.Empty);
    }

    [Test]
    public void A_channel_a_provider_replaces_is_not_considered_for_unreachability()
    {
        var classifier = new RequestClassifier(
            new StaticOptionsMonitor<LiveViewOptions>(_options), [], [new StubMetricProvider("later")]);
        Channel("first", prefixes: ["/"], sortOrder: 10);
        Channel("later", prefixes: ["/later"], sortOrder: 20);

        Assert.That(classifier.UnreachableChannels(), Is.Empty);
    }

    [TestCase("brand-a.com", true)]
    [TestCase("BRAND-A.com", true)]
    [TestCase("brand-a.com:8080", true)]
    [TestCase("www.brand-a.com", false)]
    [TestCase("brand-b.com", false)]
    [TestCase("", false)]
    public void A_host_channel_counts_only_its_hosts(string host, bool counted)
    {
        Channel("brand-a", prefixes: ["/"]).Hosts = ["brand-a.com"];

        IEnumerable<string> expected = counted ? ["brand-a"] : [];

        Assert.That(Channels(Get("/page", host)), Is.EqualTo(expected));
    }

    [TestCase("shop.brand-a.com", true)]
    [TestCase("brand-a.com", false)]
    [TestCase("shop.brand-b.com", false)]
    public void A_wildcard_host_matches_subdomains_only(string host, bool counted)
    {
        Channel("brand-a", prefixes: ["/"]).Hosts = ["*.brand-a.com"];

        IEnumerable<string> expected = counted ? ["brand-a"] : [];

        Assert.That(Channels(Get("/page", host)), Is.EqualTo(expected));
    }

    [Test]
    public void A_channel_with_only_hosts_counts_every_path_on_those_hosts()
    {
        Channel("brand-a", prefixes: []).Hosts = ["brand-a.com"];

        Assert.That(Channels(Get("/page", "brand-a.com")), Is.EqualTo(["brand-a"]));
        Assert.That(Channels(Get("/", "brand-a.com")), Is.EqualTo(["brand-a"]));
        Assert.That(Channels(Get("/page", "brand-b.com")), Is.Empty);
    }

    [Test]
    public void A_hosts_only_channel_still_honours_its_excluded_prefixes()
    {
        LiveViewChannelOptions channel = Channel("brand-a", prefixes: []);
        channel.Hosts = ["brand-a.com"];
        channel.ExcludePathPrefixes = ["/media"];

        Assert.That(Channels(Get("/media/x.jpg", "brand-a.com")), Is.Empty);
        Assert.That(Channels(Get("/page", "brand-a.com")), Is.EqualTo(["brand-a"]));
    }

    [Test]
    public void Each_site_counts_in_its_own_channel_and_the_rest_in_web()
    {
        Channel("brand-a", prefixes: ["/"], sortOrder: 10).Hosts = ["brand-a.com"];
        Channel("web", prefixes: ["/"], sortOrder: 20);

        Assert.That(Channels(Get("/page", "brand-a.com")), Is.EqualTo(["brand-a"]));
        Assert.That(Channels(Get("/page", "brand-b.com")), Is.EqualTo(["web"]));
    }

    [Test]
    public void A_request_predicate_narrows_a_channels_paths()
    {
        Channel("checkout", prefixes: ["/shop"]).RequestPredicate = c => c.Request.Query.ContainsKey("basket");

        DefaultHttpContext withBasket = Get("/shop");
        withBasket.Request.QueryString = new QueryString("?basket=1");
        DefaultHttpContext otherPath = Get("/about");
        otherPath.Request.QueryString = new QueryString("?basket=1");

        Assert.That(Channels(withBasket), Is.EqualTo(["checkout"]));
        Assert.That(Channels(Get("/shop")), Is.Empty);
        Assert.That(Channels(otherPath), Is.Empty);
    }

    [Test]
    public void A_channel_with_only_a_request_predicate_counts_any_path_it_accepts()
    {
        Channel("app", prefixes: []).RequestPredicate = c => c.Request.Headers.ContainsKey("X-App-Version");
        DefaultHttpContext withHeader = Get("/anything");
        withHeader.Request.Headers["X-App-Version"] = "2";

        Assert.That(Channels(withHeader), Is.EqualTo(["app"]));
        Assert.That(Channels(Get("/anything")), Is.Empty);
    }

    [Test]
    public void A_channel_with_no_paths_hosts_or_predicate_counts_no_requests()
    {
        Channel("signalr", prefixes: []);

        Assert.That(_classifier.Classify(Get("/anything").Request).IsTracked, Is.False);
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

    private static string RepositoryFile(string name)
    {
        for (DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, name);
            if (File.Exists(path) && File.Exists(Path.Combine(dir.FullName, "Umbricians.LiveView.slnx")))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"{name} wasn't found above {TestContext.CurrentContext.TestDirectory}.");
    }

    private static DefaultHttpContext WithUserAgent(string userAgent)
    {
        DefaultHttpContext context = Get("/page");
        context.Request.Headers.UserAgent = userAgent;
        return context;
    }

    private static DefaultHttpContext Get(string path, string? host = null)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(host))
        {
            context.Request.Host = new HostString(host);
        }

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        return context;
    }
}
