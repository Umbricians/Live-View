namespace Umbricians.LiveView.Demo.DemoMembers;

/// <summary>
/// Test members for trying the Live View members line, bound from <c>LiveViewDemo:Members</c>.
/// Demo only: not part of the package.
/// </summary>
public class DemoMembersOptions
{
    public int Count { get; set; }

    /// <summary>Shared password for every test member. Set in appsettings.Development.json only.</summary>
    public string? Password { get; set; }

    /// <summary>Format string for each member's email, given its number.</summary>
    public string EmailPattern { get; set; } = "member{0:00}@example.test";

    public IEnumerable<(string Email, string Name)> Accounts =>
        Enumerable.Range(1, Count).Select(i => (string.Format(EmailPattern, i), $"Test Member {i:00}"));
}
