using RealtimePix.BankLedger.Application;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class BankCommandIntegrationHandler(ProcessBankCommandHandler handler) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = CommandTypes.All;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var context = new MessageContext(
            envelope.CorrelationId ?? envelope.EventId.ToString("N"),
            envelope.EventId.ToString("N"));

        return envelope.EventType switch
        {
            CommandTypes.DebitFunds => handler.HandleDebitAsync(
                envelope.DeserializePayload<DebitFundsPayload>(),
                context,
                cancellationToken),
            CommandTypes.CreditFunds => handler.HandleCreditAsync(
                envelope.DeserializePayload<CreditFundsPayload>(),
                context,
                cancellationToken),
            CommandTypes.RefundFunds => handler.HandleRefundAsync(
                envelope.DeserializePayload<RefundFundsPayload>(),
                context,
                cancellationToken),
            _ => Task.CompletedTask
        };
    }
}
