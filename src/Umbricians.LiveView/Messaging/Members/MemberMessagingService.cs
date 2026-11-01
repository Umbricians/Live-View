using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbricians.LiveView.Presence;

namespace Umbricians.LiveView.Messaging.Members;

public sealed record MemberMessageRequest(
    string Headline,
    string Body,
    MemberMessageType Type,
    MemberMessageAudience Audience,
    IReadOnlyCollection<Guid> SelectedMemberKeys,
    IReadOnlyCollection<string> SenderAliases,
    DateTimeOffset ExpiresAt);

public sealed record MemberMessageSenderResult(string Alias, string Name, int? Delivered, string? Note, string? Error);

public sealed record MemberMessageSendResult(Guid Key, MemberMessageAudience Audience, int? Recipients, IReadOnlyList<MemberMessageSenderResult> Results);

/// <summary>
/// Works out who a member message is for, then hands it to each chosen <see cref="IMemberMessageSender"/>.
/// </summary>
public sealed class MemberMessagingService
{
    private static readonly TimeSpan SenderTimeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IMemberMessageSender> _senders;
    private readonly MemberPresence _presence;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemberMessagingService> _logger;

    public MemberMessagingService(
        IEnumerable<IMemberMessageSender> senders,
        MemberPresence presence,
        TimeProvider timeProvider,
        ILogger<MemberMessagingService> logger)
    {
        _senders = senders.ToList();
        _presence = presence;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IReadOnlyList<IMemberMessageSender> Senders => _senders;

    public async Task<MemberMessageSendResult> SendAsync(MemberMessageRequest request, string? senderName, CancellationToken cancellationToken)
    {
        IMemberMessageSender[] chosen = _senders
            .Where(s => request.SenderAliases.Contains(s.Alias, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        // "All online" is resolved here, at the moment of sending, rather than from what the browser last loaded.
        IReadOnlyList<Guid> memberKeys = request.Audience switch
        {
            MemberMessageAudience.Selected => request.SelectedMemberKeys.Distinct().ToList(),
            MemberMessageAudience.AllOnline => _presence.AllOnline(),
            _ => [],
        };

        var message = new MemberMessage(
            Guid.NewGuid(),
            request.Headline,
            request.Body,
            request.Type,
            request.Audience,
            memberKeys,
            senderName,
            _timeProvider.GetUtcNow(),
            request.ExpiresAt);

        MemberMessageSenderResult[] results = await Task.WhenAll(chosen.Select(s => SendWithAsync(s, message, cancellationToken)));

        _logger.LogInformation(
            "{SenderName} sent member message {MessageKey} to {Audience} ({Recipients}) via {Senders}",
            senderName,
            message.Key,
            message.Audience,
            message.Audience == MemberMessageAudience.AllMembers ? "every member" : $"{memberKeys.Count} members",
            string.Join(", ", chosen.Select(s => s.Alias)));

        return new MemberMessageSendResult(
            message.Key,
            message.Audience,
            message.Audience == MemberMessageAudience.AllMembers ? null : memberKeys.Count,
            results);
    }

    private async Task<MemberMessageSenderResult> SendWithAsync(IMemberMessageSender sender, MemberMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SenderTimeout);

        try
        {
            MemberMessageResult result = await sender.SendAsync(message, timeout.Token).WaitAsync(timeout.Token);
            return new MemberMessageSenderResult(sender.Alias, sender.Name, result.Delivered, result.Note, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MemberMessageSenderResult(sender.Alias, sender.Name, null, null, "Timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Member message sender {Alias} failed", sender.Alias);
            return new MemberMessageSenderResult(sender.Alias, sender.Name, null, null, "Failed");
        }
    }
}
