using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.LiveView.Configuration;
using Umbricians.LiveView.Messaging.Members;
using Umbricians.LiveView.Security;

namespace Umbricians.LiveView.Controllers;

/// <summary>
/// Sends messages to website members through the site's registered <see cref="IMemberMessageSender"/>s.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Live View")]
[BackOfficeRoute("umbriciansliveview/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[LiveViewPermission(Constants.Permissions.View)]
[JsonOptionsName(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)]
internal class LiveViewMemberMessagesController : ControllerBase
{
    private readonly MemberMessagingService _messaging;
    private readonly IMemberService _memberService;
    private readonly IBackOfficeSecurityAccessor _security;
    private readonly IOptionsMonitor<LiveViewOptions> _options;

    public LiveViewMemberMessagesController(
        MemberMessagingService messaging,
        IMemberService memberService,
        IBackOfficeSecurityAccessor security,
        IOptionsMonitor<LiveViewOptions> options)
    {
        _messaging = messaging;
        _memberService = memberService;
        _security = security;
        _options = options;
    }

    /// <summary>The registered delivery methods (none means member messaging is unavailable) and the member count.</summary>
    [HttpGet("member-messaging")]
    [ProducesResponseType<MemberMessagingResponseModel>(StatusCodes.Status200OK)]
    public MemberMessagingResponseModel Capabilities() => new()
    {
        Senders = _messaging.Senders
            .Select(s => new MemberMessageSenderResponseModel
            {
                Alias = s.Alias,
                Name = s.Name,
                Description = s.Description,
                ReachesOfflineMembers = s.ReachesOfflineMembers,
            })
            .ToList(),
        TotalMembers = _memberService.GetCount(MemberCountType.All),
        TimeZone = MemberMessageExpiry.TimeZoneId(_options.CurrentValue.MessageTimeZone),
    };

    [HttpPost("member-messages")]
    [LiveViewPermission(Constants.Permissions.MessageMembers)]
    [ProducesResponseType<MemberMessageSendResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send(SendMemberMessageRequestModel request, CancellationToken cancellationToken)
    {
        MemberSendResult result = await _messaging.SendAsync(
            new MemberMessageRequest(
                request.Headline,
                request.Message,
                request.Color,
                request.Audience,
                request.MemberKeys,
                request.Senders,
                request.ExpiresAt),
            _security.BackOfficeSecurity?.CurrentUser?.Name,
            cancellationToken);

        return result switch
        {
            MemberSendResult.Sent sent => Ok(sent.Result),
            MemberSendResult.Rejected rejected => MessageNotSent.Because(rejected.Reason),
            _ => throw new InvalidOperationException($"Unexpected send result {result.GetType().Name}."),
        };
    }
}

internal class MemberMessagingResponseModel
{
    public required IReadOnlyList<MemberMessageSenderResponseModel> Senders { get; init; }

    public required int TotalMembers { get; init; }

    /// <summary>The IANA id of the zone "midnight tonight" is worked out in, or null for the sender's own.</summary>
    public string? TimeZone { get; init; }
}

internal class MemberMessageSenderResponseModel
{
    public required string Alias { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required bool ReachesOfflineMembers { get; init; }
}

internal class SendMemberMessageRequestModel
{
    public string? Headline { get; init; }

    public string? Message { get; init; }

    /// <summary>One of <c>default</c>, <c>positive</c>, <c>warning</c> or <c>danger</c>.</summary>
    public string? Color { get; init; }

    /// <summary><c>selected</c>, <c>allOnline</c> or <c>allMembers</c>.</summary>
    public string? Audience { get; init; }

    /// <summary>The members to send to when <see cref="Audience"/> is <c>selected</c>.</summary>
    public Guid[]? MemberKeys { get; init; }

    /// <summary>Aliases of the senders to deliver with.</summary>
    public string[]? Senders { get; init; }

    /// <summary>When the message stops being relevant: in the future, and no more than 30 days away.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
