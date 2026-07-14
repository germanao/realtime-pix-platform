using RealtimePix.Contracts;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class SagaMessagePublisher(
    IIntegrationEventPublisher eventPublisher,
    IIntegrationMessagePublisher messagePublisher) : ISagaMessagePublisher
{
    private const string Producer = "transaction-service";

    public Task PublishDebitCommandAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        messagePublisher.PublishCommandAsync(
            BankIds.QueueName(saga.SenderBankId),
            CommandTypes.DebitFunds,
            1,
            Producer,
            new DebitFundsPayload(
                saga.TransferId,
                saga.SenderUserId,
                saga.SenderAccountId,
                saga.SenderBankId,
                saga.RecipientUserId,
                saga.RecipientAccountId,
                saga.RecipientBankId,
                saga.Amount.Value,
                TransferSagaPresentation.SimulationModeName(saga.SimulationMode)),
            subject: saga.TransferId,
            correlationId: saga.TransferId,
            causationId: transition.TransitionId,
            cancellationToken: cancellationToken);

    public Task PublishCreditCommandAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        messagePublisher.PublishCommandAsync(
            BankIds.QueueName(saga.RecipientBankId),
            CommandTypes.CreditFunds,
            1,
            Producer,
            new CreditFundsPayload(
                saga.TransferId,
                saga.RecipientUserId,
                saga.RecipientAccountId,
                saga.RecipientBankId,
                saga.SenderUserId,
                saga.SenderAccountId,
                saga.SenderBankId,
                saga.Amount.Value,
                TransferSagaPresentation.SimulationModeName(saga.SimulationMode)),
            subject: saga.TransferId,
            correlationId: saga.TransferId,
            causationId: transition.TransitionId,
            cancellationToken: cancellationToken);

    public Task PublishRefundCommandAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        messagePublisher.PublishCommandAsync(
            BankIds.QueueName(saga.SenderBankId),
            CommandTypes.RefundFunds,
            1,
            Producer,
            new RefundFundsPayload(
                saga.TransferId,
                saga.SenderUserId,
                saga.SenderAccountId,
                saga.SenderBankId,
                saga.Amount.Value,
                saga.FailureReason ?? "Credit did not complete.",
                TransferSagaPresentation.SimulationModeName(saga.SimulationMode)),
            subject: saga.TransferId,
            correlationId: saga.TransferId,
            causationId: transition.TransitionId,
            cancellationToken: cancellationToken);

    public Task PublishLegacyRequestedAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        PublishEventAsync(
            EventTypes.PixTransferRequested,
            1,
            new PixTransferRequestedPayload(
                saga.TransferId,
                saga.IdempotencyKey,
                saga.SenderUserId,
                saga.SenderAccountId,
                saga.RecipientUserId,
                saga.RecipientAccountId,
                saga.Amount.Value),
            saga,
            transition,
            cancellationToken);

    public Task PublishTransitionAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        PublishEventAsync(
            EventTypes.SagaTransitionRecorded,
            1,
            new SagaTransitionRecordedPayload(
                transition.TransitionId,
                saga.TransferId,
                transition.PreviousState is null ? null : TransferSagaPresentation.StateName(transition.PreviousState.Value),
                TransferSagaPresentation.StateName(transition.NextState),
                transition.TriggeringMessageType,
                transition.Reason,
                transition.RecordedAt),
            saga,
            transition,
            cancellationToken);

    public async Task PublishCompletedAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken)
    {
        var completedAt = saga.CompletedAt ?? saga.UpdatedAt;
        await PublishEventAsync(
            EventTypes.PixTransferCompletedV2,
            2,
            new PixTransferCompletedV2Payload(
                saga.TransferId,
                saga.SenderUserId,
                saga.RecipientUserId,
                saga.Amount.Value,
                completedAt),
            saga,
            transition,
            cancellationToken);
        await PublishEventAsync(
            EventTypes.PixTransferCompleted,
            1,
            new PixTransferCompletedPayload(
                saga.TransferId,
                saga.SenderUserId,
                saga.RecipientUserId,
                saga.Amount.Value,
                completedAt),
            saga,
            transition,
            cancellationToken);
    }

    public async Task PublishFailedAsync(
        TransferSaga saga,
        SagaTransition transition,
        bool requiresManualIntervention,
        CancellationToken cancellationToken)
    {
        var failedAt = saga.CompletedAt ?? saga.UpdatedAt;
        var reason = saga.FailureReason ?? "Transfer failed.";
        await PublishEventAsync(
            EventTypes.PixTransferFailedV2,
            2,
            new PixTransferFailedV2Payload(
                saga.TransferId,
                saga.SenderUserId,
                saga.RecipientUserId,
                saga.Amount.Value,
                reason,
                requiresManualIntervention,
                failedAt),
            saga,
            transition,
            cancellationToken);
        await PublishEventAsync(
            EventTypes.PixTransferFailed,
            1,
            new PixTransferFailedPayload(
                saga.TransferId,
                saga.SenderUserId,
                saga.RecipientUserId,
                saga.Amount.Value,
                reason,
                failedAt),
            saga,
            transition,
            cancellationToken);
    }

    public async Task PublishCompensatedAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken)
    {
        await PublishEventAsync(
            EventTypes.PixTransferCompensated,
            1,
            new PixTransferCompensatedPayload(
                saga.TransferId,
                saga.SenderUserId,
                saga.RecipientUserId,
                saga.Amount.Value,
                saga.FailureReason ?? "Credit did not complete.",
                saga.CompensatedAt ?? saga.UpdatedAt),
            saga,
            transition,
            cancellationToken);
        await PublishFailedAsync(saga, transition, false, cancellationToken);
    }

    public Task PublishTimedOutAsync(
        TransferSaga saga,
        SagaTransition transition,
        string timedOutState,
        CancellationToken cancellationToken) =>
        PublishEventAsync(
            EventTypes.PixSagaTimedOut,
            1,
            new PixSagaTimedOutPayload(saga.TransferId, timedOutState, transition.RecordedAt),
            saga,
            transition,
            cancellationToken);

    private Task PublishEventAsync<TPayload>(
        string eventType,
        int version,
        TPayload payload,
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken) =>
        eventPublisher.PublishAsync(
            eventType,
            version,
            Producer,
            payload,
            correlationId: saga.TransferId,
            causationId: transition.TransitionId,
            cancellationToken: cancellationToken);
}
