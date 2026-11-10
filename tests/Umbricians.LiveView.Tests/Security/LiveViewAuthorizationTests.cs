using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Models.Membership.Permissions;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.Security;
using Permissions = Umbricians.LiveView.Constants.Permissions;

namespace Umbricians.LiveView.Tests.Security;

/// <summary>Who may call each Live View endpoint.</summary>
public class LiveViewAuthorizationTests
{
    private static Type[] Controllers() =>
        typeof(LiveViewControllerFeatureProvider).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

    private static MethodInfo[] Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<HttpMethodAttribute>() is not null)
            .ToArray();

    [Test]
    public void Every_controller_needs_a_signed_in_backoffice_user_and_no_section()
    {
        Type[] controllers = Controllers();

        Assert.That(controllers.Length, Is.GreaterThanOrEqualTo(3));
        foreach (Type controller in controllers)
        {
            string[] policies = controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy!).ToArray();
            Assert.That(policies, Is.EqualTo([AuthorizationPolicies.BackOfficeAccess]));

            foreach (MethodInfo method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.That(method.GetCustomAttributes<AuthorizeAttribute>(), Is.Empty);
                Assert.That(method.GetCustomAttributes<AllowAnonymousAttribute>(), Is.Empty);
            }
        }
    }

    [Test]
    public void Each_endpoint_needs_its_Live_View_permissions()
    {
        var actual = new List<string>();
        foreach (Type controller in Controllers())
        {
            string[] classPermissions = controller.GetCustomAttributes<LiveViewPermissionAttribute>().Select(a => a.Permission).ToArray();
            foreach (MethodInfo action in Actions(controller))
            {
                HttpMethodAttribute http = action.GetCustomAttribute<HttpMethodAttribute>()!;
                string[] permissions = classPermissions
                    .Concat(action.GetCustomAttributes<LiveViewPermissionAttribute>().Select(a => a.Permission))
                    .Distinct()
                    .Order()
                    .ToArray();
                actual.Add($"{http.HttpMethods.Single()} {http.Template}: {string.Join(", ", permissions)}");
            }
        }

        string[] expected =
        [
            $"GET dashboard-settings: {Permissions.View}",
            $"GET snapshot: {Permissions.View}",
            $"GET backoffice-users: {Permissions.View}",
            $"GET members: {Permissions.View}",
            $"GET member-messaging: {Permissions.View}",
            $"POST messages: {string.Join(", ", new[] { Permissions.MessageUsers, Permissions.View }.Order())}",
            $"GET message-recipients: {string.Join(", ", new[] { Permissions.MessageUsers, Permissions.View }.Order())}",
            $"POST member-messages: {string.Join(", ", new[] { Permissions.MessageMembers, Permissions.View }.Order())}",
            "GET heartbeat: ",
            "DELETE heartbeat: ",
            "GET messages/{key:guid}: ",
        ];

        Assert.That(actual.Order(), Is.EqualTo(expected.Order()));
    }

    [Test]
    public void A_user_with_the_permission_but_no_Settings_access_is_let_through()
    {
        IUser user = UserIn(Group("editors", ["content"], [Permissions.View]));

        Assert.That(Authorize(Permissions.View, user), Is.Null);
    }

    [Test]
    public void A_user_without_the_permission_is_refused()
    {
        IUser user = UserIn(Group("editors", ["content"], [Permissions.MessageUsers]));

        AssertForbidden(Authorize(Permissions.View, user));
    }

    [Test]
    public void Any_one_of_the_users_groups_can_grant_the_permission()
    {
        IUser user = UserIn(Group("writers", ["content"], []), Group("watchers", ["content"], [Permissions.View]));

        Assert.That(Authorize(Permissions.View, user), Is.Null);
    }

    [Test]
    public void Settings_access_alone_is_not_enough()
    {
        IUser user = UserIn(Group("settings", ["settings"], []));

        AssertForbidden(Authorize(Permissions.View, user));
    }

    [Test]
    public void No_signed_in_user_is_refused() =>
        AssertForbidden(Authorize(Permissions.View, null));

    private static void AssertForbidden(IActionResult? result)
    {
        Assert.That(result, Is.TypeOf<ObjectResult>());
        var objectResult = (ObjectResult)result!;
        Assert.That(objectResult.StatusCode, Is.EqualTo(403));
        Assert.That(objectResult.Value, Is.TypeOf<ProblemDetails>());
        var problem = (ProblemDetails)objectResult.Value!;
        Assert.That(problem.Status, Is.EqualTo(403));
    }

    private static IActionResult? Authorize(string permission, IUser? user)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IBackOfficeSecurityAccessor>(new FakeAccessor(user))
            .BuildServiceProvider();
        var filter = (IAuthorizationFilter)new LiveViewPermissionAttribute(permission).CreateInstance(services);
        var context = new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), []);

        filter.OnAuthorization(context);

        return context.Result;
    }

    private static int _nextGroupId;

    private static ReadOnlyUserGroup Group(string alias, string[] sections, string[] permissions) =>
        new(
            ++_nextGroupId, Guid.NewGuid(), alias, null, null, null, null, alias, [], sections,
            new HashSet<string>(permissions), new HashSet<IGranularPermission>(), true);

    private static User UserIn(params ReadOnlyUserGroup[] groups)
    {
        var user = new User(new GlobalSettings());
        foreach (ReadOnlyUserGroup group in groups)
        {
            user.AddGroup(group);
        }

        return user;
    }

    private sealed class FakeAccessor(IUser? user) : IBackOfficeSecurityAccessor
    {
        public IBackOfficeSecurity? BackOfficeSecurity { get; } = new FakeSecurity(user);
    }

    private sealed class FakeSecurity(IUser? user) : IBackOfficeSecurity
    {
        public IUser? CurrentUser { get; } = user;

        public Attempt<int> GetUserId() => throw new NotSupportedException();

        public bool IsAuthenticated() => CurrentUser is not null;

        public bool UserHasSectionAccess(string section, IUser user) => throw new NotSupportedException();
    }
}
