using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Web.Common.Authorization;
using Umbricians.LiveView.Controllers;

namespace Umbricians.LiveView.Tests.Controllers;

public class LiveViewControllerBaseTests
{
    [Test]
    public void Requires_a_signed_in_backoffice_user_on_every_controller()
    {
        var authorize = typeof(LiveViewControllerBase).GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorize?.Policy, Is.EqualTo(AuthorizationPolicies.BackOfficeAccess));
        Assert.That(typeof(AuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.Inherited, Is.True);
    }

    [Test]
    public void Puts_every_controller_in_the_package_api_document()
    {
        var mapToApi = typeof(LiveViewControllerBase).GetCustomAttribute<MapToApiAttribute>();

        Assert.That(mapToApi?.ApiName, Is.EqualTo(Constants.ApiName));
    }

    // Without it the endpoints fall back to MVC's JSON settings, which change the casing the client reads.
    [Test]
    public void Uses_the_backoffice_json_settings_on_every_controller()
    {
        var jsonOptions = typeof(LiveViewControllerBase).GetCustomAttribute<JsonOptionsNameAttribute>();

        Assert.That(jsonOptions?.JsonOptionsName, Is.EqualTo(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice));
    }

    // The feature provider registers every controller in the assembly, so one that skipped the base would be
    // reachable without its authorization.
    [Test]
    public void Every_controller_inherits_the_base()
    {
        Type[] controllers = typeof(LiveViewControllerBase).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        Assert.That(controllers, Is.Not.Empty);
        using (Assert.EnterMultipleScope())
        {
            foreach (Type controller in controllers)
            {
                Assert.That(controller.IsSubclassOf(typeof(LiveViewControllerBase)), Is.True, $"{controller.Name} does not inherit LiveViewControllerBase.");
            }
        }
    }
}
