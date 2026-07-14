using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Domain;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class BankEventPublisher(
    IIntegrationEventPublisher publisher,
    BankDescriptor bank) : IBankEventPublisher
{
    private string Producer => $"{bank.BankId}-ledger-service";

    public Task AccountCreatedAsync(AccountDto account, MessageContext context, CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.AccountCreated,
            new AccountCreatedPayload(account.AccountId, account.UserId, account.BankName, account.Balance, account.BankId),
            context,
            cancellationToken);

    public Task FundsDepositedAsync(LedgerEntryDto entry, MessageContext context, CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsDeposited,
            new FundsDepositedPayload(
                entry.LedgerEntryId,
                entry.AccountId,
                entry.UserId,
                entry.Amount,
                entry.BalanceAfter,
                entry.Description,
                entry.BankId),
            context,
            cancellationToken);

    public Task FundsDebitedAsync(
        DebitFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsDebited,
            new FundsDebitedPayload(
                command.TransferId,
                command.SenderBankId,
                command.SenderAccountId,
                command.SenderUserId,
                command.Amount,
                result.Balance),
            context,
            cancellationToken);

    public Task DebitRejectedAsync(
        DebitFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsDebitRejected,
            new FundsDebitRejectedPayload(
                command.TransferId,
                command.SenderBankId,
                command.SenderAccountId,
                command.SenderUserId,
                command.Amount,
                result.Reason ?? "Debit rejected."),
            context,
            cancellationToken);

    public Task FundsCreditedAsync(
        CreditFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsCredited,
            new FundsCreditedPayload(
                command.TransferId,
                command.RecipientBankId,
                command.RecipientAccountId,
                command.RecipientUserId,
                command.Amount,
                result.Balance),
            context,
            cancellationToken);

    public Task CreditRejectedAsync(
        CreditFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsCreditRejected,
            new FundsCreditRejectedPayload(
                command.TransferId,
                command.RecipientBankId,
                command.RecipientAccountId,
                command.RecipientUserId,
                command.Amount,
                result.Reason ?? "Credit rejected."),
            context,
            cancellationToken);

    public Task FundsRefundedAsync(
        RefundFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsRefunded,
            new FundsRefundedPayload(
                command.TransferId,
                command.SenderBankId,
                command.SenderAccountId,
                command.SenderUserId,
                command.Amount,
                result.Balance),
            context,
            cancellationToken);

    public Task RefundRejectedAsync(
        RefundFundsPayload command,
        LedgerOperationResult result,
        MessageContext context,
        CancellationToken cancellationToken) =>
        PublishAsync(
            EventTypes.FundsRefundRejected,
            new FundsRefundRejectedPayload(
                command.TransferId,
                command.SenderBankId,
                command.SenderAccountId,
                command.SenderUserId,
                command.Amount,
                result.Reason ?? "Refund rejected."),
            context,
            cancellationToken);

    private Task PublishAsync<TPayload>(
        string eventType,
        TPayload payload,
        MessageContext context,
        CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            eventType,
            1,
            Producer,
            payload,
            correlationId: context.CorrelationId,
            causationId: context.CausationId,
            cancellationToken: cancellationToken);
}
