using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Umbricians.LiveView.Controllers;

/// <summary>
/// Lets MVC find Live View's controllers, which are internal so they stay out of the package's public API. MVC's own
/// provider only finds public ones, so this one adds the rest of ours and nothing else.
/// </summary>
internal sealed class LiveViewControllerFeatureProvider : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo) =>
        typeInfo.Assembly == typeof(LiveViewControllerFeatureProvider).Assembly
        && !typeInfo.IsPublic
        && typeInfo is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
        && typeof(ControllerBase).IsAssignableFrom(typeInfo);
}
