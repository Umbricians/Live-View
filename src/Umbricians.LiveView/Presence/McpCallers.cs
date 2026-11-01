using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Umbricians.LiveView.Presence;

/// <summary>
/// Reports people using MCP to Live View, so they appear in its online tables with an MCP tag. Requests to an MCP
/// endpoint the site hosts are reported for you when they are signed in; use this for an MCP server that runs
/// elsewhere, which only your code knows the caller of.
/// </summary>
public interface ILiveViewPresence
{
    /// <summary>
    /// Records that the backoffice user or member with this key used MCP just now. Keys that are neither are ignored.
    /// They stay online for the <c>mcp</c> channel's activity window after the last report.
    /// </summary>
    Task McpCallerSeenAsync(Guid key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Works out who an MCP caller is, a backoffice user or a member, by looking their key up (users first), and tells
/// the matching presence module. What each key turned out to be is remembered for a while, so busy callers are
/// looked up once.
/// </summary>
internal sealed class McpCallers : ILiveViewPresence
{
    internal static readonly TimeSpan RememberFor = TimeSpan.FromMinutes(5);
    private const int MaxRemembered = 10_000;

    private readonly ConcurrentDictionary<Guid, (Caller Caller, long Until)> _known = new();
    private readonly BackofficePresence _backofficePresence;
    private readonly MemberPresence _memberPresence;
    private readonly IBackofficeUserDirectory _users;
    private readonly IMemberDirectory _members;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McpCallers> _logger;

    internal McpCallers(
        BackofficePresence backofficePresence,
        MemberPresence memberPresence,
        IBackofficeUserDirectory users,
        IMemberDirectory members,
        TimeProvider timeProvider,
        ILogger<McpCallers> logger)
    {
        _backofficePresence = backofficePresence;
        _memberPresence = memberPresence;
        _users = users;
        _members = members;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private enum Caller
    {
        Nobody,
        BackofficeUser,
        Member,
    }

    public async Task McpCallerSeenAsync(Guid key, CancellationToken cancellationToken = default)
    {
        switch (await FindAsync(key))
        {
            case Caller.BackofficeUser:
                _backofficePresence.McpSeen(key);
                break;
            case Caller.Member:
                _memberPresence.McpSeen(key);
                break;
        }
    }

    /// <summary>
    /// Follows one MCP request to the site, recording whoever is signed in to it; dispose it once the request has
    /// run. By the time the response starts the caller has signed in, so a long-lived stream lists them while it is
    /// open; a response that only starts once the request has run records them as seen then.
    /// </summary>
    internal IAsyncDisposable Begin(HttpContext context)
    {
        var call = new McpCall(this, context);
        context.Response.OnStarting(call.StartedAsync);
        return call;
    }

    /// <summary>Records a finished MCP request by whoever is signed in. Requests nobody is signed in to are ignored.</summary>
    private Task SeenAsync(ClaimsPrincipal user) =>
        PrincipalKey.Resolve(user, _members.FindKey) is { } key ? McpCallerSeenAsync(key) : Task.CompletedTask;

    /// <summary>
    /// Records an open MCP request, such as a stream, by whoever is signed in. They stay online until the returned
    /// hold is disposed; null when nobody known is signed in.
    /// </summary>
    private async Task<IDisposable?> OpenedAsync(ClaimsPrincipal user)
    {
        if (PrincipalKey.Resolve(user, _members.FindKey) is not { } key)
        {
            return null;
        }

        return await FindAsync(key) switch
        {
            Caller.BackofficeUser => _backofficePresence.McpOpened(key),
            Caller.Member => _memberPresence.McpOpened(key),
            _ => null,
        };
    }

    private async Task<Caller> FindAsync(Guid key)
    {
        long now = _timeProvider.GetUtcNow().UtcTicks;
        if (_known.TryGetValue(key, out (Caller Caller, long Until) known) && known.Until > now)
        {
            return known.Caller;
        }

        Caller caller;
        try
        {
            caller = (await _users.GetAsync([key])).Count > 0 ? Caller.BackofficeUser
                : (await _members.GetAsync([key])).Count > 0 ? Caller.Member
                : Caller.Nobody;
        }
        catch (Exception ex)
        {
            // The lookup runs inside the caller's MCP request, which must not fail because of it. Not remembered,
            // so the next request tries again.
            _logger.LogWarning(ex, "Live View could not look up MCP caller {Key}", key);
            return Caller.Nobody;
        }

        if (_known.Count >= MaxRemembered)
        {
            _known.Clear();
        }

        _known[key] = (caller, now + RememberFor.Ticks);
        return caller;
    }

    /// <summary>One MCP request: the hold keeping its caller online while it is open, and whether it has finished.</summary>
    private sealed class McpCall : IAsyncDisposable
    {
        private readonly McpCallers _callers;
        private readonly HttpContext _context;
        private IDisposable? _hold;
        private bool _finished;

        public McpCall(McpCallers callers, HttpContext context)
        {
            _callers = callers;
            _context = context;
        }

        public async Task StartedAsync()
        {
            if (!_finished)
            {
                _hold = await _callers.OpenedAsync(_context.User);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _finished = true;
            if (_hold is { } hold)
            {
                hold.Dispose();
            }
            else
            {
                await _callers.SeenAsync(_context.User);
            }
        }
    }
}
