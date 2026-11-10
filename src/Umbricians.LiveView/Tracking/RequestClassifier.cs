using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Metrics;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Decides which channels a request counts in, under what session key, and who it shows was seen. Requests are
/// offered to the channels in sort order and counted by each whose paths, hosts and request predicate they match, up to the first exclusive
/// one. Bots, <c>OPTIONS</c> and <c>HEAD</c> requests, and static files on channels that ignore them are not counted.
/// </summary>
internal sealed partial class RequestClassifier
{
    private readonly IOptionsMonitor<LiveViewOptions> _options;
    private readonly IEnumerable<ILiveMetricProvider> _providers;

    private readonly Dictionary<string, ILiveSessionKeySource> _sessionKeySources = new(StringComparer.OrdinalIgnoreCase);

    public RequestClassifier(
        IOptionsMonitor<LiveViewOptions> options,
        IEnumerable<ILiveSessionKeySource> sessionKeySources,
        IEnumerable<ILiveMetricProvider> providers)
    {
        _options = options;
        _providers = providers;

        // The first registration of an alias wins, and the built-in names always mean the built-in sources.
        foreach (ILiveSessionKeySource source in sessionKeySources)
        {
            if (!IsBuiltIn(source.Alias))
            {
                _sessionKeySources.TryAdd(source.Alias, source);
            }
        }
    }

    public RequestClassification Classify(HttpRequest request)
    {
        LiveViewOptions options = _options.CurrentValue;
        if (!options.TrackingEnabled
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsHead(request.Method)
            || (options.IgnoreBots && BotUserAgent().IsMatch(request.Headers.UserAgent.ToString())))
        {
            return RequestClassification.Untracked;
        }

        var sessions = new List<ChannelSession>();
        var signedIn = new List<(string Channel, string[] Sources)>();
        bool memberSeen = false, backofficeSeen = false, mcpCall = false;

        foreach ((string channel, string[] sources) in Match(request, options, _providers))
        {
            // Member and backoffice presence keep their lines themselves, always keyed on the signed-in member or user.
            if (channel == Constants.Channels.Members)
            {
                memberSeen = true;
                continue;
            }

            if (channel == Constants.Channels.Backoffice)
            {
                backofficeSeen = true;
                continue;
            }

            mcpCall |= channel == Constants.Channels.Mcp;

            // Sources that use the signed-in user are only known once authentication has run further down the pipeline.
            if (ReadOnceSignedIn(sources))
            {
                signedIn.Add((channel, sources));
            }
            else if (SessionKey(request, sources) is { } sessionKey)
            {
                sessions.Add(new ChannelSession(channel, sessionKey));
            }
        }

        return new RequestClassification(this, request, sessions, signedIn, memberSeen, backofficeSeen, mcpCall);
    }

    private static IEnumerable<(string Channel, string[] Sources)> Match(
        HttpRequest request, LiveViewOptions options, IEnumerable<ILiveMetricProvider> providers)
    {
        string path = request.Path.Value ?? "/";
        bool isStaticFile = IsStaticFile(path, options.StaticFileExtensions);
        foreach (ChannelSettings channel in ChannelSettings.InOrder(options, providers))
        {
            if (!channel.Enabled || !Matches(channel, request, path))
            {
                continue;
            }

            if (!(channel.IgnoreStaticFiles && isStaticFile))
            {
                yield return (channel.Alias, channel.SessionKeys);
            }

            if (channel.Exclusive)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// A channel counts requests when it has path prefixes, hosts, a request predicate, or any mix of them. A request
    /// must then fall under one of its path prefixes (when it has any) and outside its excluded ones, be for one of its
    /// hosts (when it lists any), and pass its predicate (when it has one).
    /// </summary>
    private static bool Matches(ChannelSettings channel, HttpRequest request, string path) =>
        (channel.PathPrefixes.Length > 0 || channel.Hosts.Length > 0 || channel.RequestPredicate is not null)
        && (channel.PathPrefixes.Length == 0 || MatchesAny(path, channel.PathPrefixes))
        && !MatchesAny(path, channel.ExcludePathPrefixes)
        && (channel.Hosts.Length == 0 || MatchesHost(request.Host, channel.Hosts))
        && (channel.RequestPredicate is null || channel.RequestPredicate(request.HttpContext));

    /// <summary>The host without its port, matched exactly or, for a <c>*.</c> pattern, as any subdomain.</summary>
    private static bool MatchesHost(HostString requestHost, string[] hosts)
    {
        string host = requestHost.HasValue ? requestHost.Host : "";
        foreach (string pattern in hosts)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal)
                    ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
                    : host.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAny(string path, string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            string trimmed = prefix.TrimEnd('/');
            if (trimmed.Length == 0)
            {
                return true;
            }

            if (path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                && (path.Length == trimmed.Length || path[trimmed.Length] == '/'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStaticFile(string path, string[] extensions)
    {
        string extension = Path.GetExtension(path);
        return extension.Length > 0 && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The first source that has a value, prefixed with the source so values from different sources never collide.</summary>
    internal string? SessionKey(HttpRequest request, string[] sources)
    {
        foreach (string source in sources)
        {
            (string Source, string? Value) resolved = ResolveSource(request, source);
            if (!string.IsNullOrEmpty(resolved.Value))
            {
                return resolved.Source + ":" + resolved.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one session-key source, a built-in one or one the site registered, matched without regard to case. Returns the source in its canonical casing,
    /// so the same session keys the same way however the source was written in configuration.
    /// </summary>
    private (string Source, string? Value) ResolveSource(HttpRequest request, string source)
    {
        if (source.Equals(Constants.SessionKeySources.User, StringComparison.OrdinalIgnoreCase))
        {
            return (Constants.SessionKeySources.User, ResolveUserId(request.HttpContext.User));
        }

        if (source.Equals(Constants.SessionKeySources.Client, StringComparison.OrdinalIgnoreCase))
        {
            return (Constants.SessionKeySources.Client,
                $"{request.HttpContext.Connection.RemoteIpAddress}|{request.Headers.UserAgent}");
        }

        if (source.Equals(Constants.SessionKeySources.Authorization, StringComparison.OrdinalIgnoreCase))
        {
            return (Constants.SessionKeySources.Authorization, request.Headers.Authorization.ToString());
        }

        if (source.StartsWith(Constants.SessionKeySources.HeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string name = source[Constants.SessionKeySources.HeaderPrefix.Length..];
            return (Constants.SessionKeySources.HeaderPrefix + name, request.Headers[name].ToString());
        }

        if (source.StartsWith(Constants.SessionKeySources.CookiePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string name = source[Constants.SessionKeySources.CookiePrefix.Length..];
            return (Constants.SessionKeySources.CookiePrefix + name, request.Cookies[name]);
        }

        if (_sessionKeySources.TryGetValue(source, out ILiveSessionKeySource? custom))
        {
            return (custom.Alias, custom.GetSessionKey(request.HttpContext));
        }

        return (source, null);
    }

    private bool ReadOnceSignedIn(string[] sources) =>
        sources.Any(s => s.Equals(Constants.SessionKeySources.User, StringComparison.OrdinalIgnoreCase)
            || (_sessionKeySources.TryGetValue(s, out ILiveSessionKeySource? custom) && custom.UsesSignedInUser));

    private static bool IsBuiltIn(string source) =>
        source.Equals(Constants.SessionKeySources.User, StringComparison.OrdinalIgnoreCase)
        || source.Equals(Constants.SessionKeySources.Client, StringComparison.OrdinalIgnoreCase)
        || source.Equals(Constants.SessionKeySources.Authorization, StringComparison.OrdinalIgnoreCase)
        || source.StartsWith(Constants.SessionKeySources.HeaderPrefix, StringComparison.OrdinalIgnoreCase)
        || source.StartsWith(Constants.SessionKeySources.CookiePrefix, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.Identity.GetUserKey()?.ToString() ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;

    [GeneratedRegex(@"bot|crawl|spider|slurp|facebookexternalhit|preview|monitor|pingdom|headless|lighthouse", RegexOptions.IgnoreCase)]
    private static partial Regex BotUserAgent();
}

/// <summary>A session a request counts in: the channel, and the session key it counts under.</summary>
internal sealed record ChannelSession(string Channel, string SessionKey);

/// <summary>What the <see cref="RequestClassifier"/> made of one request.</summary>
internal sealed class RequestClassification
{
    public static readonly RequestClassification Untracked = new(null, null, [], [], false, false, false);

    private readonly RequestClassifier? _classifier;
    private readonly HttpRequest? _request;
    private readonly IReadOnlyList<(string Channel, string[] Sources)> _signedIn;

    internal RequestClassification(
        RequestClassifier? classifier,
        HttpRequest? request,
        IReadOnlyList<ChannelSession> sessions,
        IReadOnlyList<(string Channel, string[] Sources)> signedIn,
        bool memberSeen,
        bool backofficeSeen,
        bool mcpCall)
    {
        _classifier = classifier;
        _request = request;
        _signedIn = signedIn;
        Sessions = sessions;
        MemberSeen = memberSeen;
        BackofficeSeen = backofficeSeen;
        McpCall = mcpCall;
    }

    /// <summary>Sessions whose keys are known before the request runs; they count for as long as it does.</summary>
    public IReadOnlyList<ChannelSession> Sessions { get; }

    /// <summary>Whether the signed-in member should be recorded by member presence.</summary>
    public bool MemberSeen { get; }

    /// <summary>Whether the signed-in user should be recorded by backoffice presence.</summary>
    public bool BackofficeSeen { get; }

    /// <summary>Whether the request is to an MCP endpoint, whose caller is looked up once they have signed in.</summary>
    public bool McpCall { get; }

    public bool IsTracked => Sessions.Count > 0 || _signedIn.Count > 0 || MemberSeen || BackofficeSeen || McpCall;

    /// <summary>
    /// Sessions of channels with a source that uses the signed-in user, read once authentication has run. A channel whose sources
    /// all come up empty is left out.
    /// </summary>
    public IEnumerable<ChannelSession> SignedInSessions()
    {
        foreach ((string channel, string[] sources) in _signedIn)
        {
            if (_classifier!.SessionKey(_request!, sources) is { } sessionKey)
            {
                yield return new ChannelSession(channel, sessionKey);
            }
        }
    }
}
