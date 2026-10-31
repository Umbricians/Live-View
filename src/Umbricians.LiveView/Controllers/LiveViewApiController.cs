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
public class LiveViewApiController : ControllerBase
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
                LastSeen = u.LastSeen,
                OpenTabs = u.OpenTabs,
                IsConnected = u.IsConnected,
                IsCurrentUser = u.Key == currentUserKey,
            })
            .ToList();

        return new OnlineBackofficeUsersResponseModel
        {
            Total = total,
            Items = items,
            Recipients = _messenger.CountAllOnlineRecipients(currentUserKey),
        };
    }

    /// <summary>One page of the members who are logged in on the website, newest arrivals first.</summary>
    [HttpGet("members")]
    [ProducesResponseType<PagedViewModel<OnlineMemberResponseModel>>(StatusCodes.Status200OK)]
    public async Task<PagedViewModel<OnlineMemberResponseModel>> Members(int skip = 0, int take = 25)
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
                LastSeen = m.LastSeen,
            })
            .ToList();

        return new PagedViewModel<OnlineMemberResponseModel> { Total = total, Items = items };
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

public class LiveViewSnapshotResponseModel
{
    public required DateTimeOffset Timestamp { get; init; }

    public required IReadOnlyList<LiveMetricReading> Metrics { get; init; }
}

public class OnlineBackofficeUsersResponseModel : PagedViewModel<OnlineBackofficeUserResponseModel>
{
    /// <summary>How many users a message to everyone online reaches: everyone online except the current user.</summary>
    public required long Recipients { get; init; }
}

public class OnlineBackofficeUserResponseModel
{
    public required Guid Key { get; init; }

    public required string Name { get; init; }

    public string? Email { get; init; }

    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Backoffice tabs the user has open, counted from their realtime connections, or heartbeats without them.</summary>
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
