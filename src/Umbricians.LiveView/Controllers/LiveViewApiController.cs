using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Metrics;
using Umbricians.LiveView.Presence;
using Umbricians.LiveView.Security;

namespace Umbricians.LiveView.Controllers;

[ApiController]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Live View")]
[BackOfficeRoute("umbriciansliveview/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[LiveViewPermission(Constants.Permissions.View)]
[JsonOptionsName(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)]
internal class LiveViewApiController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly LiveMetricsService _metrics;
    private readonly BackofficePresence _presence;
    private readonly MemberPresence _memberPresence;
    private readonly BackofficeMessenger _messenger;
    private readonly IBackOfficeSecurityAccessor _security;

    public LiveViewApiController(
        LiveMetricsService metrics,
        BackofficePresence presence,
        MemberPresence memberPresence,
        BackofficeMessenger messenger,
        IBackOfficeSecurityAccessor security)
    {
        _metrics = metrics;
        _presence = presence;
        _memberPresence = memberPresence;
        _messenger = messenger;
        _security = security;
    }

    [HttpGet("snapshot")]
    [ProducesResponseType<LiveViewSnapshotResponseModel>(StatusCodes.Status200OK)]
    public async Task<LiveViewSnapshotResponseModel> Snapshot(CancellationToken cancellationToken) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Metrics = await _metrics.GetSnapshotAsync(cancellationToken),
    };

    /// <summary>One page of the backoffice users who are online, newest arrivals first.</summary>
    [HttpGet("backoffice-users")]
    [ProducesResponseType<OnlineBackofficeUsersResponseModel>(StatusCodes.Status200OK)]
    public async Task<OnlineBackofficeUsersResponseModel> BackofficeUsers(int skip = 0, int take = 25)
    {
        (int total, IReadOnlyList<OnlineBackofficeUser> page) = await _presence.PageAsync(
            Math.Max(skip, 0), Math.Clamp(take, 1, MaxPageSize));
        Guid? currentUserKey = _security.BackOfficeSecurity?.CurrentUser?.Key;

        List<OnlineBackofficeUserResponseModel> items = page
            .Select(u => new OnlineBackofficeUserResponseModel
            {
                Key = u.Key,
                Name = u.Name,
                Email = u.Email,
                AvatarUrls = u.AvatarUrls,
                LastSeen = u.LastSeen,
                OpenTabs = u.OpenTabs,
                IsConnected = u.IsConnected,
                IsCurrentUser = u.Key == currentUserKey,
                BackofficeLastSeen = u.BackofficeLastSeen,
                McpLastSeen = u.McpLastSeen,
            })
            .ToList();

        return new OnlineBackofficeUsersResponseModel
        {
            Total = total,
            Items = items,
            AnyOnMcp = _presence.AnyOnMcp(),
            Recipients = _messenger.CountAllOnlineRecipients(currentUserKey),
        };
    }

    /// <summary>One page of the members who are logged in on the website, newest arrivals first.</summary>
    [HttpGet("members")]
    [ProducesResponseType<OnlineMembersResponseModel>(StatusCodes.Status200OK)]
    public async Task<OnlineMembersResponseModel> Members(int skip = 0, int take = 25)
    {
        (int total, IReadOnlyList<OnlineMember> page) = await _memberPresence.PageAsync(
            Math.Max(skip, 0), Math.Clamp(take, 1, MaxPageSize));

        List<OnlineMemberResponseModel> items = page
            .Select(m => new OnlineMemberResponseModel
            {
                Key = m.Key,
                Name = m.Name,
                Email = m.Email,
                MemberType = m.MemberType,
                AvatarUrl = m.AvatarUrl,
                LastSeen = m.LastSeen,
                WebsiteLastSeen = m.WebsiteLastSeen,
                McpLastSeen = m.McpLastSeen,
            })
            .ToList();

        return new OnlineMembersResponseModel { Total = total, Items = items, AnyOnMcp = _memberPresence.AnyOnMcp() };
    }

    [HttpPost("messages")]
    [LiveViewPermission(Constants.Permissions.MessageUsers)]
    [ProducesResponseType<SendBackofficeMessageResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendMessage(SendBackofficeMessageRequestModel request)
    {
        IUser? currentUser = _security.BackOfficeSecurity?.CurrentUser;
        BackofficeSendResult result = await _messenger.SendAsync(
            new BackofficeMessageRequest(request.Headline, request.Message, request.Color, request.UserKeys, request.AllOnline),
            new BackofficeSender(currentUser?.Key, currentUser?.Name));

        return result switch
        {
            BackofficeSendResult.Sent sent => Ok(new SendBackofficeMessageResponseModel { Key = sent.Message.Key, Recipients = sent.Recipients }),
            BackofficeSendResult.Rejected rejected => MessageNotSent.Because(rejected.Reason),
            _ => throw new InvalidOperationException($"Unexpected send result {result.GetType().Name}."),
        };
    }
}

internal class LiveViewSnapshotResponseModel
{
    public required DateTimeOffset Timestamp { get; init; }

    public required IReadOnlyList<LiveMetricReading> Metrics { get; init; }
}

internal class OnlineBackofficeUsersResponseModel : PagedViewModel<OnlineBackofficeUserResponseModel>
{
    /// <summary>Whether anyone online is using MCP; only then does each row need to say how its user is online.</summary>
    public required bool AnyOnMcp { get; init; }

    /// <summary>How many users a message to everyone online reaches: everyone online except the current user.</summary>
    public required long Recipients { get; init; }
}

internal class OnlineBackofficeUserResponseModel
{
    public required Guid Key { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    /// <summary>The user's avatar in Umbraco's sizes, smallest first; empty when they have not set one.</summary>
    public required IReadOnlyList<string> AvatarUrls { get; init; }

    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Backoffice tabs the user has open, counted from their realtime connections, or heartbeats without them.</summary>
    public required int OpenTabs { get; init; }

    /// <summary>Whether the user has a realtime connection, so a message reaches them straight away.</summary>
    public required bool IsConnected { get; init; }

    public required bool IsCurrentUser { get; init; }

    /// <summary>When the user was last seen in the backoffice, or null when they are only on MCP.</summary>
    public DateTimeOffset? BackofficeLastSeen { get; init; }

    /// <summary>When the user last used MCP, or null when they are not on it.</summary>
    public DateTimeOffset? McpLastSeen { get; init; }
}

internal class OnlineMembersResponseModel : PagedViewModel<OnlineMemberResponseModel>
{
    /// <summary>Whether anyone online is using MCP; only then does each row need to say how its member is online.</summary>
    public required bool AnyOnMcp { get; init; }
}

internal class OnlineMemberResponseModel
{
    public required Guid Key { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    public string? MemberType { get; init; }

    /// <summary>The member's picture from the site's avatar provider, or null to show their initials.</summary>
    public string? AvatarUrl { get; init; }

    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>When the member was last seen on the website, or null when they are only on MCP.</summary>
    public DateTimeOffset? WebsiteLastSeen { get; init; }

    /// <summary>When the member last used MCP, or null when they are not on it.</summary>
    public DateTimeOffset? McpLastSeen { get; init; }
}

internal class SendBackofficeMessageRequestModel
{
    public string? Headline { get; init; }

    public string? Message { get; init; }

    /// <summary>One of <c>default</c>, <c>positive</c>, <c>warning</c> or <c>danger</c>.</summary>
    public string? Color { get; init; }

    public Guid[]? UserKeys { get; init; }

    /// <summary>Send to every backoffice user who is online, ignoring <see cref="UserKeys"/>.</summary>
    public bool AllOnline { get; init; }
}

internal class SendBackofficeMessageResponseModel
{
    public required Guid Key { get; init; }

    public required int Recipients { get; init; }
}
