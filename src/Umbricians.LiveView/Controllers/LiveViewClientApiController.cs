using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Controllers;

/// <summary>
/// Called by every backoffice, whatever sections the user can access: keeps the user listed as online and
/// hands over messages sent to them.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Live View")]
[BackOfficeRoute("umbriciansliveview/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[JsonOptionsName(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)]
internal class LiveViewClientApiController : ControllerBase
{
    private readonly BackofficeMessenger _messenger;
    private readonly BackofficePresence _presence;
    private readonly IBackOfficeSecurityAccessor _security;

    public LiveViewClientApiController(
        BackofficeMessenger messenger,
        BackofficePresence presence,
        IBackOfficeSecurityAccessor security)
    {
        _messenger = messenger;
        _presence = presence;
        _security = security;
    }

    /// <summary>
    /// Sent once a minute by every open backoffice tab, so idle editors still show as online and each tab is
    /// counted. Returns messages the user has not received yet.
    /// </summary>
    /// <param name="tab">A random id the tab picks when it loads.</param>
    [HttpGet("heartbeat")]
    [ProducesResponseType<HeartbeatResponseModel>(StatusCodes.Status200OK)]
    public ActionResult<HeartbeatResponseModel> Heartbeat([FromQuery] string? tab)
    {
        if (_security.BackOfficeSecurity?.CurrentUser?.Key is not { } userKey)
        {
            return Unauthorized();
        }

        if (IsValidTabId(tab))
        {
            _presence.TabHeartbeat(userKey, tab);
        }

        return new HeartbeatResponseModel { PendingMessages = _messenger.GetUndelivered(userKey) };
    }

    /// <summary>Sent by a tab as it closes, so it stops counting straight away.</summary>
    [HttpDelete("heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult EndTab([FromQuery] string? tab)
    {
        if (_security.BackOfficeSecurity?.CurrentUser?.Key is { } userKey && IsValidTabId(tab))
        {
            _presence.TabClosed(userKey, tab);
        }

        return NoContent();
    }

    private static bool IsValidTabId([NotNullWhen(true)] string? tab) =>
        tab is { Length: > 0 and <= 64 } && tab.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    [HttpGet("messages/{key:guid}")]
    [ProducesResponseType<BackofficeMessage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BackofficeMessage> Message(Guid key)
    {
        if (_security.BackOfficeSecurity?.CurrentUser?.Key is not { } userKey
            || _messenger.Receive(key, userKey) is not { } message)
        {
            return NotFound();
        }

        return message;
    }
}

internal class HeartbeatResponseModel
{
    public required IReadOnlyList<Guid> PendingMessages { get; init; }
}
