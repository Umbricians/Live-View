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

    [Fact]
    public void The_package_has_five_health_checks()
    {
        Assert.Equal(
            ["ForwardedHeadersCheck", "MemberAvatarPropertyCheck", "MemberMessagingCheck", "TrackingEnabledCheck", "UnreachableChannelsCheck"],
            Checks.Select(t => t.Name).Order());
    }

    [Fact]
    public void Every_health_check_is_in_the_Live_View_group_with_a_description()
    {
        foreach (Type check in Checks)
        {
            HealthCheckAttribute attribute = check.GetCustomAttribute<HealthCheckAttribute>()!;

            Assert.Equal("Live View", attribute.Group);
            Assert.False(string.IsNullOrWhiteSpace(attribute.Description), check.Name);
        }
    }

    [Fact]
    public void The_ids_are_distinct()
    {
        string[] ids = Checks.Select(t => t.GetCustomAttribute<HealthCheckAttribute>()!.Id.ToString()).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
