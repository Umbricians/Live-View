using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbricians.LiveView.Controllers;

namespace Umbricians.LiveView.Tests.Controllers;

public class LiveViewControllerBaseTests
{
    [Fact]
    public void Requires_a_signed_in_backoffice_user_on_every_controller()
    {
        var authorize = typeof(LiveViewControllerBase).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(AuthorizationPolicies.BackOfficeAccess, authorize?.Policy);
        Assert.True(typeof(AuthorizeAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.Inherited);
    }

    [Fact]
    public void Puts_every_controller_in_the_package_api_document()
    {
        var mapToApi = typeof(LiveViewControllerBase).GetCustomAttribute<MapToApiAttribute>();

        Assert.Equal(Constants.ApiName, mapToApi?.ApiName);
    }

    // The feature provider registers every controller in the assembly, so one that skipped the base would be
    // reachable without its authorization.
    [Fact]
    public void Every_controller_inherits_the_base()
    {
        Type[] controllers = typeof(LiveViewControllerBase).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(controllers);
        Assert.All(controllers, controller => Assert.True(
            controller.IsSubclassOf(typeof(LiveViewControllerBase)), $"{controller.Name} does not inherit LiveViewControllerBase."));
    }
}
