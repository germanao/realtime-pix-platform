using System.Text.Json;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class TransferStoreTests
{
    [Fact]
    public void Same_idempotency_key_returns_existing_transfer()
    {
        var store = new global::TransferStore();
        var request = CreateRequest("idempotency-1");

        var first = store.Create(request);
        var second = store.Create(request);

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.Transfer.TransferId, second.Transfer.TransferId);
    }

    [Fact]
    public async Task Duplicate_credit_success_completes_transfer_once()
    {
        var store = new global::TransferStore();
        var transfer = store.Create(CreateRequest("idempotency-1")).Transfer;
        var publisher = new RecordingPublisher();
        var handler = new global::PixTransferOutcomeHandler(store, publisher, new global::NoopTransactionalOperation());
        var payload = new PixCreditSucceededPayload(
            transfer.TransferId,
            transfer.RecipientUserId,
            transfer.RecipientAccountId,
            transfer.Amount,
            25m);
        var envelope = CreateEnvelope(EventTypes.PixCreditSucceeded, payload, transfer.TransferId);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(publisher.Events, item => item.EventType == EventTypes.PixTransferCompleted);
        Assert.Equal("completed", store.Get(transfer.TransferId)!.Status);
    }

    [Fact]
    public async Task Duplicate_debit_failure_marks_transfer_failed_once()
    {
        var store = new global::TransferStore();
        var transfer = store.Create(CreateRequest("idempotency-1")).Transfer;
        var publisher = new RecordingPublisher();
        var handler = new global::PixTransferOutcomeHandler(store, publisher, new global::NoopTransactionalOperation());
        var payload = new PixDebitFailedPayload(
            transfer.TransferId,
            transfer.SenderUserId,
            transfer.SenderAccountId,
            transfer.Amount,
            "Insufficient fictional funds.");
        var envelope = CreateEnvelope(EventTypes.PixDebitFailed, payload, transfer.TransferId);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(publisher.Events, item => item.EventType == EventTypes.PixTransferFailed);
        var current = store.Get(transfer.TransferId)!;
        Assert.Equal("failed", current.Status);
        Assert.Equal("Insufficient fictional funds.", current.FailureReason);
    }

    private static global::PixTransferRequest CreateRequest(string idempotencyKey)
    {
        return new global::PixTransferRequest(
            idempotencyKey,
            "sender",
            "sender_bank-a",
            "recipient",
            "recipient_bank-a",
            25m);
    }

    private static EventEnvelope CreateEnvelope<TPayload>(string eventType, TPayload payload, string correlationId)
    {
        return new EventEnvelope(
            Guid.NewGuid(),
            eventType,
            1,
            DateTimeOffset.UtcNow,
            correlationId,
            null,
            "test",
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Options));
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<PublishedEvent> Events { get; } = [];

        public Task PublishAsync<TPayload>(
            string eventType,
            int version,
            string producer,
            TPayload payload,
            string? correlationId = null,
            string? causationId = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new PublishedEvent(eventType, producer, payload));
            return Task.CompletedTask;
        }
    }

    private sealed record PublishedEvent(string EventType, string Producer, object? Payload);
}
