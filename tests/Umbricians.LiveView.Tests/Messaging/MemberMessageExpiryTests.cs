using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Tests.Messaging;

public class MemberMessageExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 21, 0, 0, TimeSpan.Zero);

    [Test]
    public void An_expiry_in_the_future_is_kept()
    {
        Assert.That(MemberMessageExpiry.TryResolve(Now.AddHours(3), Now, out DateTimeOffset expiresAt), Is.True);
        Assert.That(expiresAt, Is.EqualTo(Now.AddHours(3)));
    }

    [TestCase(0)]
    [TestCase(-60)]
    public void An_expiry_that_has_already_passed_is_turned_away(int minutes)
    {
        Assert.That(MemberMessageExpiry.TryResolve(Now.AddMinutes(minutes), Now, out _), Is.False);
    }

    [Test]
    public void A_missing_expiry_is_turned_away()
    {
        Assert.That(MemberMessageExpiry.TryResolve(null, Now, out _), Is.False);
    }

    [Test]
    public void An_expiry_past_the_longest_is_moved_back_to_it()
    {
        Assert.That(MemberMessageExpiry.TryResolve(Now.AddDays(30).AddMinutes(2), Now, out DateTimeOffset expiresAt), Is.True);
        Assert.That(expiresAt, Is.EqualTo(Now.AddDays(30)));
    }

    [TestCase("America/Denver", "America/Denver")]
    [TestCase(" Europe/London ", "Europe/London")]
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("Not/AZone", null)]
    public void The_time_zone_is_given_as_an_iana_id(string? configured, string? expected)
    {
        Assert.That(MemberMessageExpiry.TimeZoneId(configured), Is.EqualTo(expected));
    }

    [Test]
    public void A_windows_time_zone_id_is_given_as_its_iana_id()
    {
        Assert.That(MemberMessageExpiry.TimeZoneId("Mountain Standard Time"), Is.EqualTo("America/Denver"));
    }
}
