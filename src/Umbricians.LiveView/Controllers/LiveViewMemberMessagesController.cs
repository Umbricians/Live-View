using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
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
public class LiveViewMemberMessagesController : ControllerBase
{
    private static readonly Dictionary<string, MemberMessageType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = MemberMessageType.Information,
        ["warning"] = MemberMessageType.Warning,
        ["danger"] = MemberMessageType.Urgent,
        ["positive"] = MemberMessageType.GoodNews,
    };

    private static readonly Dictionary<string, MemberMessageAudience> Audiences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["selected"] = MemberMessageAudience.Selected,
        ["allOnline"] = MemberMessageAudience.AllOnline,
        ["allMembers"] = MemberMessageAudience.AllMembers,
    };

    private const int MaxSelectedMembers = 10_000;

    private readonly MemberMessagingService _messaging;
    private readonly IMemberService _memberService;
    private readonly IBackOfficeSecurityAccessor _security;

    public LiveViewMemberMessagesController(
        MemberMessagingService messaging,
        IMemberService memberService,
        IBackOfficeSecurityAccessor security)
    {
        _messaging = messaging;
        _memberService = memberService;
        _security = security;
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
    };

    [HttpPost("member-messages")]
    [LiveViewPermission(Constants.Permissions.MessageMembers)]
    [ProducesResponseType<MemberMessageSendResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send(SendMemberMessageRequestModel request, CancellationToken cancellationToken)
    {
        string headline = request.Headline?.Trim() ?? string.Empty;
        string message = request.Message?.Trim() ?? string.Empty;
        Guid[] selected = request.MemberKeys?.Distinct().ToArray() ?? [];
        string[] senders = request.Senders?.Where(a => _messaging.Senders.Any(s => s.Alias.Equals(a, StringComparison.OrdinalIgnoreCase))).ToArray() ?? [];

        if (headline.Length is 0 or > 200 || message.Length > 2000)
        {
            return MessageNotSent.Because("A headline (up to 200 characters) is required, and the message can be up to 2000 characters.");
        }

        if (!Audiences.TryGetValue(request.Audience ?? string.Empty, out MemberMessageAudience audience))
        {
            return MessageNotSent.Because("Audience must be 'selected', 'allOnline' or 'allMembers'.");
        }

        if (audience == MemberMessageAudience.Selected && selected.Length is 0 or > MaxSelectedMembers)
        {
            return MessageNotSent.Because($"Select between 1 and {MaxSelectedMembers} members, or send to all online members instead.");
        }

        if (senders.Length == 0)
        {
            return MessageNotSent.Because("Choose at least one delivery method.");
        }

        int minutes = Math.Clamp(request.ExpiresInMinutes ?? 60, 1, 60 * 24 * 30);
        MemberMessageSendResult result = await _messaging.SendAsync(
            new MemberMessageRequest(
                headline,
                message,
                Types.GetValueOrDefault(request.Color ?? string.Empty, MemberMessageType.Information),
                audience,
                selected,
                senders,
                TimeSpan.FromMinutes(minutes)),
            _security.BackOfficeSecurity?.CurrentUser?.Name,
            cancellationToken);

        return Ok(result);
    }
}

public class MemberMessagingResponseModel
{
    public required IReadOnlyList<MemberMessageSenderResponseModel> Senders { get; init; }

    public required int TotalMembers { get; init; }
}

public class MemberMessageSenderResponseModel
{
    public required string Alias { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required bool ReachesOfflineMembers { get; init; }
}

public class SendMemberMessageRequestModel
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

    /// <summary>How long the message stays relevant; defaults to an hour.</summary>
    public int? ExpiresInMinutes { get; init; }
}
