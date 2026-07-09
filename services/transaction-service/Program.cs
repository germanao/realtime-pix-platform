using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "transaction-service";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
builder.Services.AddCors();
builder.Services.AddOpenTelemetry().UseAzureMonitor();

var defaultConnectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddSingleton<TransferStore>();
    builder.Services.AddSingleton<ITransferStore>(serviceProvider =>
        new InMemoryTransferStoreAdapter(serviceProvider.GetRequiredService<TransferStore>()));
    builder.Services.AddSingleton<ITransactionalOperation, NoopTransactionalOperation>();
    builder.Services.AddSingleton<IIntegrationEventHandler, PixTransferOutcomeHandler>();
}
else
{
    builder.Services.AddDbContext<TransactionDbContext>(options => options.UseNpgsql(defaultConnectionString));
    builder.Services.AddScoped<ITransferStore, EfTransferStore>();
    builder.Services.AddScoped<ITransactionalOperation, EfTransactionalOperation>();
    builder.Services.AddScoped<IIntegrationEventHandler, PixTransferOutcomeHandler>();
}

builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);
if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddRealtimePixEfCoreEventing<TransactionDbContext>();
}

var app = builder.Build();
app.UseCors(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod());

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));

app.MapPost("/pix/transfers", async (
    PixTransferRequest request,
    ITransferStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation,
    CancellationToken cancellationToken) =>
{
    if (request.Amount <= 0)
    {
        return Results.BadRequest(new { message = "Transfer amount must be positive." });
    }

    CreateTransferResult? created = null;
    await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
    {
        created = await store.CreateAsync(request, innerCancellationToken);
        if (created.IsNew)
        {
            await publisher.PublishAsync(
                EventTypes.PixTransferRequested,
                1,
                ServiceName,
                new PixTransferRequestedPayload(
                    created.Transfer.TransferId,
                    created.Transfer.IdempotencyKey,
                    created.Transfer.SenderUserId,
                    created.Transfer.SenderAccountId,
                    created.Transfer.RecipientUserId,
                    created.Transfer.RecipientAccountId,
                    created.Transfer.Amount),
                correlationId: created.Transfer.TransferId,
                cancellationToken: innerCancellationToken);
        }
    }, cancellationToken);

    var response = created ?? throw new InvalidOperationException("Transfer creation did not produce a result.");
    return Results.Accepted($"/pix/transfers/{response.Transfer.TransferId}", response.Transfer);
});

app.MapGet("/pix/transfers/{transferId}", async (string transferId, ITransferStore store, CancellationToken cancellationToken) =>
{
    var transfer = await store.GetAsync(transferId, cancellationToken);
    return transfer is null ? Results.NotFound(new { message = "Transfer was not found." }) : Results.Ok(transfer);
});

app.Run();

public sealed record PixTransferRequest(
    string? IdempotencyKey,
    string SenderUserId,
    string SenderAccountId,
    string RecipientUserId,
    string RecipientAccountId,
    decimal Amount);

public sealed record TransferResponse(
    string TransferId,
    string IdempotencyKey,
    string SenderUserId,
    string SenderAccountId,
    string RecipientUserId,
    string RecipientAccountId,
    decimal Amount,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTransferResult(bool IsNew, TransferResponse Transfer);

public sealed record TransferTransitionResult(bool Changed, TransferResponse? Transfer);

public interface ITransferStore
{
    Task<CreateTransferResult> CreateAsync(PixTransferRequest request, CancellationToken cancellationToken);

    Task<TransferResponse?> GetAsync(string transferId, CancellationToken cancellationToken);

    Task<TransferTransitionResult> TryMarkDebitedAsync(string transferId, CancellationToken cancellationToken);

    Task<TransferTransitionResult> TryCompleteAsync(string transferId, CancellationToken cancellationToken);

    Task<TransferTransitionResult> TryFailAsync(string transferId, string reason, CancellationToken cancellationToken);
}

public interface ITransactionalOperation
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public sealed class NoopTransactionalOperation : ITransactionalOperation
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        return operation(cancellationToken);
    }
}

public sealed class InMemoryTransferStoreAdapter(TransferStore inner) : ITransferStore
{
    public Task<CreateTransferResult> CreateAsync(PixTransferRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Create(request));
    }

    public Task<TransferResponse?> GetAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Get(transferId));
    }

    public Task<TransferTransitionResult> TryMarkDebitedAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.TryMarkDebited(transferId));
    }

    public Task<TransferTransitionResult> TryCompleteAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.TryComplete(transferId));
    }

    public Task<TransferTransitionResult> TryFailAsync(string transferId, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.TryFail(transferId, reason));
    }
}

public sealed class TransferStore : ITransferStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TransferState> _transfers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idempotencyIndex = new(StringComparer.OrdinalIgnoreCase);

    public Task<CreateTransferResult> CreateAsync(PixTransferRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Create(request));
    }

    public Task<TransferResponse?> GetAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Get(transferId));
    }

    public Task<TransferTransitionResult> TryMarkDebitedAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(TryMarkDebited(transferId));
    }

    public Task<TransferTransitionResult> TryCompleteAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(TryComplete(transferId));
    }

    public Task<TransferTransitionResult> TryFailAsync(string transferId, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(TryFail(transferId, reason));
    }

    public CreateTransferResult Create(PixTransferRequest request)
    {
        lock (_gate)
        {
            var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : request.IdempotencyKey.Trim();

            if (_idempotencyIndex.TryGetValue(idempotencyKey, out var existingTransferId))
            {
                return new CreateTransferResult(false, ToResponse(_transfers[existingTransferId]));
            }

            var now = DateTimeOffset.UtcNow;
            var transfer = new TransferState(
                Guid.NewGuid().ToString("N"),
                idempotencyKey,
                request.SenderUserId,
                request.SenderAccountId,
                request.RecipientUserId,
                request.RecipientAccountId,
                request.Amount,
                "requested",
                null,
                now,
                now);

            _transfers[transfer.TransferId] = transfer;
            _idempotencyIndex[idempotencyKey] = transfer.TransferId;
            return new CreateTransferResult(true, ToResponse(transfer));
        }
    }

    public TransferResponse? Get(string transferId)
    {
        lock (_gate)
        {
            return _transfers.TryGetValue(transferId, out var transfer) ? ToResponse(transfer) : null;
        }
    }

    public TransferResponse? MarkDebited(string transferId)
    {
        return TryMarkDebited(transferId).Transfer;
    }

    public TransferResponse? Complete(string transferId)
    {
        return TryComplete(transferId).Transfer;
    }

    public TransferResponse? Fail(string transferId, string reason)
    {
        return TryFail(transferId, reason).Transfer;
    }

    public TransferTransitionResult TryMarkDebited(string transferId)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var transfer))
            {
                return new TransferTransitionResult(false, null);
            }

            if (transfer.Status is "debited" or "completed" or "failed")
            {
                return new TransferTransitionResult(false, ToResponse(transfer));
            }

            transfer.Status = "debited";
            transfer.FailureReason = null;
            transfer.UpdatedAt = DateTimeOffset.UtcNow;
            return new TransferTransitionResult(true, ToResponse(transfer));
        }
    }

    public TransferTransitionResult TryComplete(string transferId)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var transfer))
            {
                return new TransferTransitionResult(false, null);
            }

            if (transfer.Status is "completed" or "failed")
            {
                return new TransferTransitionResult(false, ToResponse(transfer));
            }

            transfer.Status = "completed";
            transfer.FailureReason = null;
            transfer.UpdatedAt = DateTimeOffset.UtcNow;
            return new TransferTransitionResult(true, ToResponse(transfer));
        }
    }

    public TransferTransitionResult TryFail(string transferId, string reason)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var transfer))
            {
                return new TransferTransitionResult(false, null);
            }

            if (transfer.Status is "completed" or "failed")
            {
                return new TransferTransitionResult(false, ToResponse(transfer));
            }

            transfer.Status = "failed";
            transfer.FailureReason = reason;
            transfer.UpdatedAt = DateTimeOffset.UtcNow;
            return new TransferTransitionResult(true, ToResponse(transfer));
        }
    }

    private static TransferResponse ToResponse(TransferState transfer)
    {
        return new TransferResponse(
            transfer.TransferId,
            transfer.IdempotencyKey,
            transfer.SenderUserId,
            transfer.SenderAccountId,
            transfer.RecipientUserId,
            transfer.RecipientAccountId,
            transfer.Amount,
            transfer.Status,
            transfer.FailureReason,
            transfer.CreatedAt,
            transfer.UpdatedAt);
    }

    private sealed class TransferState(
        string transferId,
        string idempotencyKey,
        string senderUserId,
        string senderAccountId,
        string recipientUserId,
        string recipientAccountId,
        decimal amount,
        string status,
        string? failureReason,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        public string TransferId { get; } = transferId;
        public string IdempotencyKey { get; } = idempotencyKey;
        public string SenderUserId { get; } = senderUserId;
        public string SenderAccountId { get; } = senderAccountId;
        public string RecipientUserId { get; } = recipientUserId;
        public string RecipientAccountId { get; } = recipientAccountId;
        public decimal Amount { get; } = amount;
        public string Status { get; set; } = status;
        public string? FailureReason { get; set; } = failureReason;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }
}

public static class TransactionServiceMetadata
{
    public const string Name = "transaction-service";
}

public sealed class PixTransferOutcomeHandler(
    ITransferStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } =
    [
        RealtimePix.Contracts.EventTypes.PixDebitSucceeded,
        RealtimePix.Contracts.EventTypes.PixDebitFailed,
        RealtimePix.Contracts.EventTypes.PixCreditSucceeded
    ];

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
        {
            switch (envelope.EventType)
            {
                case RealtimePix.Contracts.EventTypes.PixDebitSucceeded:
                {
                    var payload = envelope.DeserializePayload<PixDebitSucceededPayload>();
                    await store.TryMarkDebitedAsync(payload.TransferId, innerCancellationToken);
                    break;
                }
                case RealtimePix.Contracts.EventTypes.PixDebitFailed:
                {
                    var payload = envelope.DeserializePayload<PixDebitFailedPayload>();
                    var failed = await store.TryFailAsync(payload.TransferId, payload.Reason, innerCancellationToken);
                    if (failed.Changed && failed.Transfer is not null)
                    {
                        await publisher.PublishAsync(
                            RealtimePix.Contracts.EventTypes.PixTransferFailed,
                            1,
                            TransactionServiceMetadata.Name,
                            new PixTransferFailedPayload(failed.Transfer.TransferId, failed.Transfer.SenderUserId, failed.Transfer.RecipientUserId, failed.Transfer.Amount, payload.Reason, DateTimeOffset.UtcNow),
                            correlationId: envelope.CorrelationId,
                            causationId: envelope.EventId.ToString("N"),
                            cancellationToken: innerCancellationToken);
                    }

                    break;
                }
                case RealtimePix.Contracts.EventTypes.PixCreditSucceeded:
                {
                    var payload = envelope.DeserializePayload<PixCreditSucceededPayload>();
                    var completed = await store.TryCompleteAsync(payload.TransferId, innerCancellationToken);
                    if (completed.Changed && completed.Transfer is not null)
                    {
                        await publisher.PublishAsync(
                            RealtimePix.Contracts.EventTypes.PixTransferCompleted,
                            1,
                            TransactionServiceMetadata.Name,
                            new PixTransferCompletedPayload(completed.Transfer.TransferId, completed.Transfer.SenderUserId, completed.Transfer.RecipientUserId, completed.Transfer.Amount, DateTimeOffset.UtcNow),
                            correlationId: envelope.CorrelationId,
                            causationId: envelope.EventId.ToString("N"),
                            cancellationToken: innerCancellationToken);
                    }

                    break;
                }
            }
        }, cancellationToken);
    }
}
