using System.Text.Json;
using RealtimePix.Contracts;

namespace RealtimeEvents.Application;

public sealed class ProjectPlatformEventHandler(
    IRealtimeProjectionStore store,
    IRealtimeProjectionNotifier notifier,
    IArchitectureFlowPublisher publisher,
    IRealtimeProjectionTransaction transaction)
{
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        var transferId = TryGetString(platformEvent.Payload, "transferId");
        var timelineItem = new TimelineEventResponse(
            platformEvent.EventId,
            platformEvent.EventType,
            platformEvent.Producer,
            transferId,
            platformEvent.CorrelationId,
            platformEvent.OccurredAt,
            platformEvent.Payload);

        var timelineAdded = false;
        var flowAdded = false;
        FlowStepResponse? step = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            timelineAdded = await store.TryAddTimelineAsync(timelineItem, innerCancellationToken);
            if (platformEvent.EventType == EventTypes.ArchitectureFlowStepRecorded)
            {
                return;
            }

            step = CreateStep(platformEvent, transferId);
            flowAdded = await store.TryAddFlowStepAsync(platformEvent.EventId, step, innerCancellationToken);
            if (flowAdded)
            {
                await publisher.PublishAsync(step, platformEvent, innerCancellationToken);
            }
        }, cancellationToken);

        if (timelineAdded)
        {
            await notifier.TimelineItemAsync(timelineItem, cancellationToken);
        }

        if (flowAdded && step is not null)
        {
            await notifier.TransferFlowStepAsync(step, cancellationToken);
        }
    }

    private static FlowStepResponse CreateStep(PlatformEvent platformEvent, string? transferId)
    {
        var stage = platformEvent.EventType switch
        {
            EventTypes.PixTransferRequested => "transaction-service",
            EventTypes.PixDebitSucceeded or EventTypes.PixDebitFailed or EventTypes.PixCreditSucceeded => "wallet-ledger-service",
            EventTypes.PixTransferCompleted or EventTypes.PixTransferFailed => "transaction-service",
            EventTypes.FundsDebited or EventTypes.FundsDebitRejected or EventTypes.FundsCredited or
                EventTypes.FundsCreditRejected or EventTypes.FundsRefunded or EventTypes.FundsRefundRejected => platformEvent.Producer,
            EventTypes.SagaTransitionRecorded or EventTypes.PixSagaTimedOut or EventTypes.PixTransferCompletedV2 or
                EventTypes.PixTransferFailedV2 or EventTypes.PixTransferCompensated => "transaction-service",
            EventTypes.FundsDeposited => platformEvent.Producer,
            EventTypes.UserPresenceChanged => "identity-presence-service",
            _ => platformEvent.Producer
        };

        var title = platformEvent.EventType
            .Replace(".v1", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".v2", string.Empty, StringComparison.OrdinalIgnoreCase);
        var detail = platformEvent.EventType switch
        {
            EventTypes.PixTransferRequested => "The transfer request was accepted and the coordinator started the Saga.",
            EventTypes.PixDebitSucceeded => "The legacy wallet persisted a debit ledger entry.",
            EventTypes.PixDebitFailed => "The legacy wallet rejected the debit.",
            EventTypes.PixCreditSucceeded => "The legacy recipient account was credited.",
            EventTypes.PixTransferCompleted => "The legacy transfer workflow completed.",
            EventTypes.PixTransferFailed => "The legacy transfer workflow failed.",
            EventTypes.FundsDebited => "The sender bank atomically recorded the debit and outcome event.",
            EventTypes.FundsDebitRejected => "The sender bank rejected the debit without changing the balance.",
            EventTypes.FundsCredited => "The recipient bank independently recorded the credit and outcome event.",
            EventTypes.FundsCreditRejected => "The recipient bank rejected the credit, so the coordinator began compensation.",
            EventTypes.FundsRefunded => "The sender bank applied the compensating refund exactly once.",
            EventTypes.FundsRefundRejected => "The sender bank rejected the refund; operator intervention is required.",
            EventTypes.SagaTransitionRecorded => "The coordinator persisted a versioned Saga state transition.",
            EventTypes.PixSagaTimedOut => "The Saga deadline expired and the coordinator selected a recovery action.",
            EventTypes.PixTransferCompletedV2 => "The orchestrated Saga completed after debit and credit succeeded.",
            EventTypes.PixTransferFailedV2 => "The orchestrated Saga reached a terminal failure state.",
            EventTypes.PixTransferCompensated => "The debit was refunded after credit failure, conserving fictional money.",
            _ => "The platform projected this event to the public real-time timeline."
        };

        var outcome = platformEvent.EventType switch
        {
            EventTypes.PixDebitFailed or EventTypes.PixTransferFailed or EventTypes.FundsDebitRejected or
                EventTypes.FundsCreditRejected or EventTypes.FundsRefundRejected or EventTypes.PixTransferFailedV2 or
                EventTypes.PixSagaTimedOut => "failure",
            EventTypes.PixTransferRequested or EventTypes.SagaTransitionRecorded => "pending",
            EventTypes.PixDebitSucceeded or EventTypes.PixCreditSucceeded or EventTypes.PixTransferCompleted or
                EventTypes.FundsDebited or EventTypes.FundsCredited or EventTypes.FundsRefunded or
                EventTypes.PixTransferCompletedV2 or EventTypes.PixTransferCompensated => "success",
            _ => "info"
        };

        return new FlowStepResponse(
            Guid.NewGuid().ToString("N"),
            transferId,
            platformEvent.EventType,
            stage,
            title,
            detail,
            DateTimeOffset.UtcNow,
            platformEvent.EventId,
            platformEvent.Producer,
            platformEvent.CorrelationId,
            platformEvent.CausationId,
            outcome);
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
}
