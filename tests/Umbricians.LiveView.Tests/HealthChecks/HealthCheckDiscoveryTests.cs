using System.Reflection;
using Umbraco.Cms.Core.HealthChecks;
using Umbricians.LiveView.Composers;

namespace Umbricians.LiveView.Tests.HealthChecks;

/// <summary>Umbraco finds health checks by scanning the assembly, so each one needs its attribute and a unique id.</summary>
public class HealthCheckDiscoveryTests
{
    private static readonly Type[] Checks = typeof(LiveViewComposer).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(HealthCheck)) && !t.IsAbstract)
        .ToArray();

    [Test]
    public void The_package_has_five_health_checks()
    {
        Assert.That(Checks.Select(t => t.Name).Order(), Is.EqualTo(["ForwardedHeadersCheck", "MemberAvatarPropertyCheck", "MemberMessagingCheck", "TrackingEnabledCheck", "UnreachableChannelsCheck"]));
    }

    [Test]
    public void Every_health_check_is_in_the_Live_View_group_with_a_description()
    {
        foreach (Type check in Checks)
        {
            HealthCheckAttribute attribute = check.GetCustomAttribute<HealthCheckAttribute>()!;

            Assert.That(attribute.Group, Is.EqualTo("Live View"));
            Assert.That(string.IsNullOrWhiteSpace(attribute.Description), Is.False, check.Name);
        }
    }

    [Test]
    public void The_ids_are_distinct()
    {
        string[] ids = Checks.Select(t => t.GetCustomAttribute<HealthCheckAttribute>()!.Id.ToString()).ToArray();

        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
    }
}
