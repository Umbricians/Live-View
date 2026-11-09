using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>
/// Session-key sources are written by hand in configuration, so they match however they are cased, and the same
/// session keys the same way whatever the casing. Sources the site registers match the same way, and never replace a
/// built-in one.
/// </summary>
public class SessionKeySourceTests
{
    private const string Channel = "test";

    private readonly LiveViewOptions _options = new();
    private readonly RequestClassifier _classifier;

    public SessionKeySourceTests() =>
        _classifier = new RequestClassifier(
            new StaticOptionsMonitor<LiveViewOptions>(_options),
            [new TenantSource(), new TenantClaimSource(), new ClientImpostor()]);

    [Theory]
    [InlineData("User")]
    [InlineData("user")]
    [InlineData("USER")]
    public void The_user_source_matches_any_casing(string source)
    {
        Guid userKey = Guid.NewGuid();
        DefaultHttpContext context = Request(source);

        RequestClassification request = _classifier.Classify(context.Request);
        // Authentication runs further down the pipeline, so the user is only known once it has.
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userKey.ToString())], "Test"));

        Assert.Empty(request.Sessions);
        Assert.Equal([new ChannelSession(Channel, $"User:{userKey}")], request.SignedInSessions());
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("client")]
    public void The_client_source_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Headers.UserAgent = "Browser/1.0";

        Assert.Equal(["Client:203.0.113.7|Browser/1.0"], SessionKeys(context));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    public void The_authorization_source_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers.Authorization = "Bearer abc";

        Assert.Equal(["Authorization:Bearer abc"], SessionKeys(context));
    }

    [Theory]
    [InlineData("Header:Mcp-Session-Id")]
    [InlineData("header:Mcp-Session-Id")]
    public void The_header_prefix_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers["Mcp-Session-Id"] = "s1";

        Assert.Equal(["Header:Mcp-Session-Id:s1"], SessionKeys(context));
    }

    [Theory]
    [InlineData("Cookie:_ga")]
    [InlineData("COOKIE:_ga")]
    public void The_cookie_prefix_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Request.Headers.Cookie = "_ga=g1";

        Assert.Equal(["Cookie:_ga:g1"], SessionKeys(context));
    }

    [Theory]
    [InlineData("Tenant")]
    [InlineData("tenant")]
    public void A_registered_source_matches_any_casing(string source)
    {
        DefaultHttpContext context = Request(source);
        context.Items["tenant"] = "acme";

        Assert.Equal(["Tenant:acme"], SessionKeys(context));
    }

    [Fact]
    public void A_later_source_is_used_when_a_registered_source_has_no_value()
    {
        DefaultHttpContext context = Request("Tenant", "Client");
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal(["Client:203.0.113.7|"], SessionKeys(context));
    }

    [Fact]
    public void A_registered_source_that_uses_the_signed_in_user_is_read_once_authentication_has_run()
    {
        DefaultHttpContext context = Request("TenantClaim");

        RequestClassification request = _classifier.Classify(context.Request);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant", "acme")], "Test"));

        Assert.Empty(request.Sessions);
        Assert.Equal([new ChannelSession(Channel, "TenantClaim:acme")], request.SignedInSessions());
    }

    [Fact]
    public void A_built_in_source_name_always_means_the_built_in_source()
    {
        DefaultHttpContext context = Request("Client");
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.Request.Headers.UserAgent = "Browser/1.0";

        Assert.Equal(["Client:203.0.113.7|Browser/1.0"], SessionKeys(context));
    }

    private IEnumerable<string> SessionKeys(DefaultHttpContext context) =>
        _classifier.Classify(context.Request).Sessions.Select(s => s.SessionKey);

    /// <summary>A GET for <c>/test</c>, tracked by a single channel keyed on <paramref name="sources"/>.</summary>
    private DefaultHttpContext Request(params string[] sources)
    {
        _options.Channels[Channel] = new LiveViewChannelOptions
        {
            PathPrefixes = ["/test"],
            SessionKeys = sources,
        };

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/test";
        return context;
    }

    /// <summary>A key the site's own code works out, here left in <see cref="HttpContext.Items"/>.</summary>
    private sealed class TenantSource : ILiveSessionKeySource
    {
        public string Alias => "Tenant";

        public string? GetSessionKey(HttpContext context) => context.Items["tenant"] as string;
    }

    /// <summary>A key read from the signed-in user's claims.</summary>
    private sealed class TenantClaimSource : ILiveSessionKeySource
    {
        public string Alias => "TenantClaim";

        public bool UsesSignedInUser => true;

        public string? GetSessionKey(HttpContext context) => context.User.FindFirst("tenant")?.Value;
    }

    /// <summary>Claims a built-in name, which stays the built-in source.</summary>
    private sealed class ClientImpostor : ILiveSessionKeySource
    {
        public string Alias => "Client";

        public string? GetSessionKey(HttpContext context) => "impostor";
    }
}
