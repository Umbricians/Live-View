using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Tests.Messaging;

public class MemberMessageExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_expiry_in_the_future_is_kept()
    {
        Assert.True(MemberMessageExpiry.TryResolve(Now.AddHours(3), Now, out DateTimeOffset expiresAt));
        Assert.Equal(Now.AddHours(3), expiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void An_expiry_that_has_already_passed_is_turned_away(int minutes)
    {
        Assert.False(MemberMessageExpiry.TryResolve(Now.AddMinutes(minutes), Now, out _));
    }

    [Fact]
    public void A_missing_expiry_is_turned_away()
    {
        Assert.False(MemberMessageExpiry.TryResolve(null, Now, out _));
    }

    [Fact]
    public void An_expiry_past_the_longest_is_moved_back_to_it()
    {
        Assert.True(MemberMessageExpiry.TryResolve(Now.AddDays(30).AddMinutes(2), Now, out DateTimeOffset expiresAt));
        Assert.Equal(Now.AddDays(30), expiresAt);
    }

    [Theory]
    [InlineData("America/Denver", "America/Denver")]
    [InlineData(" Europe/London ", "Europe/London")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Not/AZone", null)]
    public void The_time_zone_is_given_as_an_iana_id(string? configured, string? expected)
    {
        Assert.Equal(expected, MemberMessageExpiry.TimeZoneId(configured));
    }

    [Fact]
    public void A_windows_time_zone_id_is_given_as_its_iana_id()
    {
        Assert.Equal("America/Denver", MemberMessageExpiry.TimeZoneId("Mountain Standard Time"));
    }
}
