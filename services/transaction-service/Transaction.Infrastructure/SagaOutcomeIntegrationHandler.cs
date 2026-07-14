using RealtimePix.Contracts;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class SagaOutcomeIntegrationHandler(ProcessSagaOutcomeHandler handler) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } =
    [
        RealtimePix.Contracts.EventTypes.FundsDebited,
        RealtimePix.Contracts.EventTypes.FundsDebitRejected,
        RealtimePix.Contracts.EventTypes.FundsCredited,
        RealtimePix.Contracts.EventTypes.FundsCreditRejected,
        RealtimePix.Contracts.EventTypes.FundsRefunded,
        RealtimePix.Contracts.EventTypes.FundsRefundRejected
    ];

    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var metadata = new SagaMessageMetadata(
            envelope.EventId.ToString("N"),
            envelope.EventType,
            envelope.CorrelationId,
            envelope.CausationId);
        return envelope.EventType switch
        {
            RealtimePix.Contracts.EventTypes.FundsDebited => handler.HandleFundsDebitedAsync(
                envelope.DeserializePayload<FundsDebitedPayload>(),
                metadata,
                cancellationToken),
            RealtimePix.Contracts.EventTypes.FundsDebitRejected => handler.HandleDebitRejectedAsync(
                envelope.DeserializePayload<FundsDebitRejectedPayload>(),
                metadata,
                cancellationToken),
            RealtimePix.Contracts.EventTypes.FundsCredited => handler.HandleFundsCreditedAsync(
                envelope.DeserializePayload<FundsCreditedPayload>(),
                metadata,
                cancellationToken),
            RealtimePix.Contracts.EventTypes.FundsCreditRejected => handler.HandleCreditRejectedAsync(
                envelope.DeserializePayload<FundsCreditRejectedPayload>(),
                metadata,
                cancellationToken),
            RealtimePix.Contracts.EventTypes.FundsRefunded => handler.HandleFundsRefundedAsync(
                envelope.DeserializePayload<FundsRefundedPayload>(),
                metadata,
                cancellationToken),
            RealtimePix.Contracts.EventTypes.FundsRefundRejected => handler.HandleRefundRejectedAsync(
                envelope.DeserializePayload<FundsRefundRejectedPayload>(),
                metadata,
                cancellationToken),
            _ => Task.CompletedTask
        };
    }
}
