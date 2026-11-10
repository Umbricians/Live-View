using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Web.Common.Authorization;

namespace Umbricians.LiveView.Controllers;

/// <summary>
/// The base of every Live View Management API controller: signed-in backoffice users only, the backoffice's JSON
/// settings, and the package's own Swagger document.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[JsonOptionsName(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)]
[MapToApi(Constants.ApiName)]
internal abstract class LiveViewControllerBase : ControllerBase
{
}
