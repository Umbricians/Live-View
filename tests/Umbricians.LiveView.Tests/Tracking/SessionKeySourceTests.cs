using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Tracking;

namespace Umbricians.LiveView.Tests.Tracking;

/// <summary>
/// Session-key sources are written by hand in configuration, so they match however they are cased, and the same
/// session keys the same way whatever the casing.
/// </summary>
public class SessionKeySourceTests
{
    private const string Channel = "test";

    private readonly LiveViewOptions _options = new();
    private readonly RequestClassifier _classifier;

    public SessionKeySourceTests() => _classifier = new RequestClassifier(new StaticOptionsMonitor<LiveViewOptions>(_options));

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

    private IEnumerable<string> SessionKeys(DefaultHttpContext context) =>
        _classifier.Classify(context.Request).Sessions.Select(s => s.SessionKey);

    /// <summary>A GET for <c>/test</c>, tracked by a single channel keyed on <paramref name="source"/>.</summary>
    private DefaultHttpContext Request(string source)
    {
        _options.Channels[Channel] = new LiveViewChannelOptions
        {
            PathPrefixes = ["/test"],
            SessionKeys = [source],
        };

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/test";
        return context;
    }
}
