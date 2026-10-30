using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Controllers;

[ApiController]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Live View")]
[BackOfficeRoute("umbriciansliveview/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[JsonOptionsName(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)]
public class LiveViewApiController : ControllerBase
{
    private static readonly string[] MessageColors = ["default", "positive", "warning", "danger"];

    private const int MaxPageSize = 100;

    private readonly LiveMetricsService _metrics;
    private readonly BackofficePresence _presence;
    private readonly MemberPresence _memberPresence;
    private readonly BackofficeMessenger _messenger;
    private readonly IUserService _userService;
    private readonly MemberResolver _memberResolver;
    private readonly IUserConnectionManager _connections;
    private readonly IBackOfficeSecurityAccessor _security;

    public LiveViewApiController(
        LiveMetricsService metrics,
        BackofficePresence presence,
        MemberPresence memberPresence,
        BackofficeMessenger messenger,
        IUserService userService,
        MemberResolver memberResolver,
        IUserConnectionManager connections,
        IBackOfficeSecurityAccessor security)
    {
        _metrics = metrics;
        _presence = presence;
        _memberPresence = memberPresence;
        _messenger = messenger;
        _userService = userService;
        _memberResolver = memberResolver;
        _connections = connections;
        _security = security;
    }

    [HttpGet("snapshot")]
    [ProducesResponseType<LiveViewSnapshotResponseModel>(StatusCodes.Status200OK)]
    public async Task<LiveViewSnapshotResponseModel> Snapshot(CancellationToken cancellationToken) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Metrics = await _metrics.GetSnapshotAsync(cancellationToken),
    };

    /// <summary>One page of the backoffice users who are online, most recently active first.</summary>
    [HttpGet("backoffice-users")]
    [ProducesResponseType<PagedViewModel<OnlineBackofficeUserResponseModel>>(StatusCodes.Status200OK)]
    public async Task<PagedViewModel<OnlineBackofficeUserResponseModel>> BackofficeUsers(int skip = 0, int take = 25)
    {
        IReadOnlyList<OnlineBackofficeUser> online = _presence.GetOnline();
        IReadOnlyList<OnlineBackofficeUser> page = online.Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, MaxPageSize)).ToList();
        if (page.Count == 0)
        {
            return new PagedViewModel<OnlineBackofficeUserResponseModel> { Total = online.Count, Items = [] };
        }

        // Only the page being shown is looked up, however many users are online.
        Dictionary<Guid, IUser> users = (await _userService.GetAsync(page.Select(o => o.UserKey)))
            .ToDictionary(u => u.Key);
        Guid? currentUserKey = _security.BackOfficeSecurity?.CurrentUser?.Key;

        List<OnlineBackofficeUserResponseModel> items = page
            .Where(o => users.ContainsKey(o.UserKey))
            .Select(o =>
            {
                // Every open backoffice tab holds one server-events connection, which closes the moment the tab
                // does, so that is the exact tab count. Heartbeats only stand in when realtime is unavailable.
                int connections = _connections.GetConnections(o.UserKey).Count;
                return new OnlineBackofficeUserResponseModel
                {
                    Key = o.UserKey,
                    Name = users[o.UserKey].Name ?? users[o.UserKey].Username,
                    Email = users[o.UserKey].Email,
                    LastSeen = o.LastSeen,
                    OpenTabs = connections > 0 ? connections : o.OpenTabs,
                    IsConnected = connections > 0,
                    IsCurrentUser = o.UserKey == currentUserKey,
                };
            })
            .ToList();

        return new PagedViewModel<OnlineBackofficeUserResponseModel> { Total = online.Count, Items = items };
    }

    /// <summary>One page of the members who are logged in on the website, most recently active first.</summary>
    [HttpGet("members")]
    [ProducesResponseType<PagedViewModel<OnlineMemberResponseModel>>(StatusCodes.Status200OK)]
    public async Task<PagedViewModel<OnlineMemberResponseModel>> Members(int skip = 0, int take = 25)
    {
        (int total, IReadOnlyList<OnlineMember> page) = _memberPresence.GetOnline(
            Math.Max(skip, 0), Math.Clamp(take, 1, MaxPageSize));

        IReadOnlyDictionary<string, IMember> members = await _memberResolver.ResolveAsync(page.Select(m => m.MemberId));

        List<OnlineMemberResponseModel> items = page
            .Select(m => (Online: m, Member: members.GetValueOrDefault(m.MemberId)))
            .Where(x => x.Member is not null)
            .Select(x => new OnlineMemberResponseModel
            {
                Key = x.Member!.Key,
                Name = x.Member.Name ?? x.Member.Username,
                Email = x.Member.Email,
                MemberType = x.Member.ContentType.Name ?? x.Member.ContentTypeAlias,
                LastSeen = x.Online.LastSeen,
            })
            .ToList();

        return new PagedViewModel<OnlineMemberResponseModel> { Total = total, Items = items };
    }

    [HttpPost("messages")]
    [ProducesResponseType<SendBackofficeMessageResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMessage(SendBackofficeMessageRequestModel request)
    {
        string headline = request.Headline?.Trim() ?? string.Empty;
        string message = request.Message?.Trim() ?? string.Empty;
        // "All online" is resolved here rather than in the browser, which only ever holds one page of users.
        Guid[] recipients = request.AllOnline
            ? _presence.GetOnline().Select(u => u.UserKey).ToArray()
            : request.UserKeys?.Distinct().ToArray() ?? [];
        string color = request.Color is { } c && MessageColors.Contains(c) ? c : "default";

        if (headline.Length is 0 or > 200 || message.Length > 2000 || recipients.Length is 0 or > 1000)
        {
            return BadRequest("A headline (up to 200 characters), a message (up to 2000) and at least one recipient are required.");
        }

        BackofficeMessage sent = await _messenger.SendAsync(
            headline, message, color, _security.BackOfficeSecurity?.CurrentUser?.Name, recipients);

        return Ok(new SendBackofficeMessageResponseModel { Key = sent.Key, Recipients = recipients.Length });
    }
}

public class LiveViewSnapshotResponseModel
{
    public required DateTimeOffset Timestamp { get; init; }

    public required IReadOnlyList<LiveMetricReading> Metrics { get; init; }
}

public class OnlineBackofficeUserResponseModel
{
    public required Guid Key { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Backoffice tabs the user has open, counted from each tab's heartbeat.</summary>
    public required int OpenTabs { get; init; }

    /// <summary>Whether the user has a realtime connection, so a message reaches them straight away.</summary>
    public required bool IsConnected { get; init; }

    public required bool IsCurrentUser { get; init; }
}

public class OnlineMemberResponseModel
{
    public required Guid Key { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    public string? MemberType { get; init; }

    public required DateTimeOffset LastSeen { get; init; }
}

public class SendBackofficeMessageRequestModel
{
    public string? Headline { get; init; }

    public string? Message { get; init; }

    /// <summary>One of <c>default</c>, <c>positive</c>, <c>warning</c> or <c>danger</c>.</summary>
    public string? Color { get; init; }

    public Guid[]? UserKeys { get; init; }

    /// <summary>Send to every backoffice user who is online, ignoring <see cref="UserKeys"/>.</summary>
    public bool AllOnline { get; init; }
}

public class SendBackofficeMessageResponseModel
{
    public required Guid Key { get; init; }

    public required int Recipients { get; init; }
}
