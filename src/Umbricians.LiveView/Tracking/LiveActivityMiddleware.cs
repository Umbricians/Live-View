using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Umbraco.Extensions;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tracking;

/// <summary>
/// Offers each request to the channels in order, counting it in every one whose paths it falls under up to the
/// first exclusive channel. Sessions stay active for the length of the request, so long-lived streams (MCP over
/// SSE, for one) count while they are open.
/// </summary>
internal sealed partial class LiveActivityMiddleware : IMiddleware
{
    private readonly ILiveActivityTracker _tracker;
    private readonly BackofficePresence _backofficePresence;
    private readonly MemberPresence _memberPresence;
    private readonly McpCallers _mcpCallers;
    private readonly IOptionsMonitor<LiveViewOptions> _options;

    public LiveActivityMiddleware(
        ILiveActivityTracker tracker,
        BackofficePresence backofficePresence,
        MemberPresence memberPresence,
        McpCallers mcpCallers,
        IOptionsMonitor<LiveViewOptions> options)
    {
        _tracker = tracker;
        _backofficePresence = backofficePresence;
        _memberPresence = memberPresence;
        _mcpCallers = mcpCallers;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        LiveViewOptions options = _options.CurrentValue;
        List<(string Channel, string[] Sources)> matches = options.TrackingEnabled ? Match(context.Request, options) : [];
        if (matches.Count == 0)
        {
            await next(context);
            return;
        }

        // The signed-in user (or member) is only known once authentication has run further down the pipeline,
        // so channels keyed on it are counted when the request completes. The rest are held open for its length.
        var activities = new List<IDisposable>();
        McpCall? mcpCall = matches.Any(m => m.Channel == Constants.Channels.Mcp) ? new McpCall() : null;
        if (mcpCall is not null)
        {
            // By the time the response starts, the caller has signed in, so a long-lived stream shows who it is
            // while it is open rather than once it closes.
            context.Response.OnStarting(async () =>
            {
                if (!mcpCall.Finished)
                {
                    mcpCall.Hold = await _mcpCallers.OpenedAsync(context.User);
                }
            });
        }

        try
        {
            foreach ((string channel, string[] sources) in matches)
            {
                if (channel != Constants.Channels.Members
                    && channel != Constants.Channels.Backoffice
                    && !IsKeyedOnUser(sources)
                    && ResolveSessionKey(context.Request, sources) is { } sessionKey)
                {
                    activities.Add(_tracker.BeginActivity(channel, sessionKey));
                }
            }

            await next(context);
        }
        finally
        {
            activities.ForEach(a => a.Dispose());
        }

        if (mcpCall is not null)
        {
            mcpCall.Finished = true;
            if (mcpCall.Hold is { } hold)
            {
                hold.Dispose();
            }
            else
            {
                // The response had not started (it starts after this middleware returns), so record it as finished.
                await _mcpCallers.SeenAsync(context.User);
            }
        }

        foreach ((string channel, string[] sources) in matches)
        {
            // Member and backoffice presence keep their lines themselves, always keyed on the signed-in member or user.
            if (channel == Constants.Channels.Members)
            {
                _memberPresence.Seen(context.User);
            }
            else if (channel == Constants.Channels.Backoffice)
            {
                _backofficePresence.Seen(context.User);
            }
            else if (IsKeyedOnUser(sources) && ResolveSessionKey(context.Request, sources) is { } userKey)
            {
                _tracker.Touch(channel, userKey);
            }
        }
    }

    /// <summary>One MCP request: the hold keeping its caller online while it is open, and whether it has finished.</summary>
    private sealed class McpCall
    {
        public IDisposable? Hold { get; set; }

        public bool Finished { get; set; }
    }

    private static bool IsKeyedOnUser(string[] sources) =>
        sources.Contains(Constants.SessionKeySources.User, StringComparer.OrdinalIgnoreCase);

    private static List<(string Channel, string[] Sources)> Match(HttpRequest request, LiveViewOptions options)
    {
        var matches = new List<(string, string[])>();

        if (HttpMethods.IsOptions(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return matches;
        }

        if (options.IgnoreBots && BotUserAgent().IsMatch(request.Headers.UserAgent.ToString()))
        {
            return matches;
        }

        string path = request.Path.Value ?? "/";
        bool isStaticFile = IsStaticFile(path, options.StaticFileExtensions);
        foreach ((string alias, LiveViewChannelOptions channelOptions) in options.Channels.OrderBy(c => c.Value.SortOrder ?? 0))
        {
            if (!channelOptions.Enabled
                || !MatchesAny(path, channelOptions.PathPrefixes)
                || MatchesAny(path, channelOptions.ExcludePathPrefixes))
            {
                continue;
            }

            if (!(channelOptions.IgnoreStaticFiles == true && isStaticFile))
            {
                matches.Add((alias, channelOptions.SessionKeys ?? [Constants.SessionKeySources.Client]));
            }

            if (channelOptions.Exclusive ?? true)
            {
                break;
            }
        }

        return matches;
    }

    private static bool MatchesAny(string path, string[]? prefixes)
    {
        if (prefixes is null)
        {
            return false;
        }

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

    private static string? ResolveSessionKey(HttpRequest request, string[] sources)
    {
        foreach (string source in sources)
        {
            (string Source, string? Value) resolved = ResolveSource(request, source);
            if (!string.IsNullOrEmpty(resolved.Value))
            {
                // Prefix with the source so identical values from different sources never collide.
                return resolved.Source + ":" + resolved.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one session-key source, matched without regard to case. Returns the source in its canonical casing,
    /// so the same session keys the same way however the source was written in configuration.
    /// </summary>
    private static (string Source, string? Value) ResolveSource(HttpRequest request, string source)
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

        return (source, null);
    }

    private static string? ResolveUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.Identity.GetUserKey()?.ToString() ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;

    [GeneratedRegex(@"bot|crawl|spider|slurp|facebookexternalhit|preview|monitor|pingdom|headless|lighthouse", RegexOptions.IgnoreCase)]
    private static partial Regex BotUserAgent();
}
