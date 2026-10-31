using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Builders;

namespace Umbricians.LiveView.Controllers;

/// <summary>
/// The 400 for a message that can't be sent. It is ProblemDetails, the only error body the backoffice's HTTP client
/// reads, so the reason reaches the dashboard's "Message not sent" notification.
/// </summary>
internal static class MessageNotSent
{
    public static BadRequestObjectResult Because(string reason) =>
        new(new ProblemDetailsBuilder().WithTitle("Message not sent").WithDetail(reason).Build());
}
