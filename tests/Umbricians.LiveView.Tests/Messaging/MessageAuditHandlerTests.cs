using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbricians.LiveView.Messaging;
using Umbricians.LiveView.Messaging.Members;

namespace Umbricians.LiveView.Tests.Messaging;

public class MessageAuditHandlerTests
{
    private readonly RecordingAuditService _audit = new();
    private readonly MessageAuditHandler _handler;
    private readonly Guid _sender = Guid.NewGuid();

    public MessageAuditHandlerTests()
    {
        _handler = new MessageAuditHandler(_audit, NullLogger<MessageAuditHandler>.Instance);
    }

    [Fact]
    public async Task A_backoffice_message_is_audited_as_sent_by_its_sender()
    {
        var message = new BackofficeMessage(Guid.NewGuid(), "Heads up", "", "default", "Me", DateTimeOffset.UnixEpoch);

        await _handler.HandleAsync(
            new BackofficeMessageSentNotification(message, [Guid.NewGuid(), Guid.NewGuid()], _sender), CancellationToken.None);

        AuditEntry entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditType.Notify, entry.Type);
        Assert.Equal(_sender, entry.UserKey);
        Assert.Equal("Umbricians.LiveView", entry.EntityType);
        Assert.Equal("Sent a message to 2 backoffice users: Heads up", entry.Comment);
        Assert.Equal(message.Key.ToString(), entry.Parameters);
    }

    [Fact]
    public async Task A_member_message_to_every_member_is_audited_with_its_delivery_methods()
    {
        var message = new MemberMessage(
            Guid.NewGuid(), "Hello", "", MemberMessageType.Information, MemberMessageAudience.AllMembers, [], "Me",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        MemberMessageSenderResult[] results =
        [
            new("banner", "Banner", 3, null, null),
            new("email", "Email", null, null, "Failed"),
        ];

        await _handler.HandleAsync(new MemberMessageSentNotification(message, results, _sender), CancellationToken.None);

        Assert.Equal("Sent a message to every member by Banner, Email: Hello", Assert.Single(_audit.Entries).Comment);
    }

    [Fact]
    public async Task A_message_with_no_known_sender_is_not_audited_and_does_not_fail()
    {
        var message = new BackofficeMessage(Guid.NewGuid(), "Heads up", "", "default", null, DateTimeOffset.UnixEpoch);

        await _handler.HandleAsync(new BackofficeMessageSentNotification(message, [Guid.NewGuid()], null), CancellationToken.None);

        Assert.Empty(_audit.Entries);
    }

    private sealed record AuditEntry(AuditType Type, Guid UserKey, string? EntityType, string? Comment, string? Parameters);

    private sealed class RecordingAuditService : IAuditService
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task<Attempt<AuditLogOperationStatus>> AddAsync(
            AuditType type, Guid userKey, int objectId, string? entityType, string? comment = null, string? parameters = null)
        {
            Entries.Add(new AuditEntry(type, userKey, entityType, comment, parameters));
            return Task.FromResult(Attempt.Succeed(AuditLogOperationStatus.Success));
        }

        public void Add(AuditType type, int userId, int objectId, string? entityType, string comment, string? parameters = null) =>
            throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetLogs(int objectId) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetUserLogs(int userId, AuditType type, DateTime? sinceDate = null) =>
            throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetLogs(AuditType type, DateTime? sinceDate = null) => throw new NotSupportedException();

        public void CleanLogs(int maximumAgeOfLogsInMinutes) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetPagedItemsByEntity(
            int entityId, long pageIndex, int pageSize, out long totalRecords, Direction orderDirection = Direction.Descending,
            AuditType[]? auditTypeFilter = null, IQuery<IAuditItem>? customFilter = null) => throw new NotSupportedException();

        public IEnumerable<IAuditItem> GetPagedItemsByUser(
            int userId, long pageIndex, int pageSize, out long totalRecords, Direction orderDirection = Direction.Descending,
            AuditType[]? auditTypeFilter = null, IQuery<IAuditItem>? customFilter = null) => throw new NotSupportedException();

        public IAuditEntry Write(
            int performingUserId, string perfomingDetails, string performingIp, DateTime eventDateUtc, int affectedUserId,
            string? affectedDetails, string eventType, string eventDetails) => throw new NotSupportedException();
    }
}
