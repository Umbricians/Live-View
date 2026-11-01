using Microsoft.Extensions.Logging.Abstractions;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Tests.Presence;

public class AvatarLookupTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task The_providers_url_is_used()
    {
        string? url = await AvatarLookup.TryAsync(_ => Task.FromResult<string?>("/media/alice.jpg"), Timeout, NullLogger.Instance, Guid.NewGuid());

        Assert.Equal("/media/alice.jpg", url);
    }

    [Fact]
    public async Task A_provider_that_throws_means_no_avatar()
    {
        string? url = await AvatarLookup.TryAsync(
            _ => throw new InvalidOperationException("Profile service is down"), Timeout, NullLogger.Instance, Guid.NewGuid());

        Assert.Null(url);
    }

    [Fact]
    public async Task A_provider_that_fails_later_means_no_avatar()
    {
        string? url = await AvatarLookup.TryAsync(
            async _ =>
            {
                await Task.Yield();
                throw new HttpRequestException("Profile service is down");
            },
            Timeout,
            NullLogger.Instance,
            Guid.NewGuid());

        Assert.Null(url);
    }

    [Fact]
    public async Task A_provider_that_runs_out_of_time_means_no_avatar_even_if_it_ignores_cancellation()
    {
        var neverFinishes = new TaskCompletionSource<string?>();

        string? url = await AvatarLookup.TryAsync(_ => neverFinishes.Task, Timeout, NullLogger.Instance, Guid.NewGuid());

        Assert.Null(url);
    }
}
