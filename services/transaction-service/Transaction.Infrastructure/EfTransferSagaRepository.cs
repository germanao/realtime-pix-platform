using Microsoft.EntityFrameworkCore;
using Npgsql;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class EfTransferSagaRepository(TransactionSagaDbContext dbContext) : ITransferSagaRepository
{
    public async Task AddAsync(
        TransferSaga saga,
        SagaTransition transition,
        CancellationToken cancellationToken)
    {
        dbContext.TransferSagas.Add(ToEntity(saga));
        dbContext.SagaTransitions.Add(ToEntity(transition));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(saga.IdempotencyKey);
        }
    }

    public async Task<TransferSaga?> GetAsync(string transferId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.TransferSagas.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TransferId == transferId, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<TransferSaga?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TransferSagas.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task SaveTransitionAsync(
        TransferSaga saga,
        int expectedVersion,
        SagaTransition transition,
        CancellationToken cancellationToken)
    {
        var entity = ToEntity(saga);
        dbContext.TransferSagas.Attach(entity);
        dbContext.Entry(entity).State = EntityState.Modified;
        dbContext.Entry(entity).Property(item => item.Version).OriginalValue = expectedVersion;
        dbContext.SagaTransitions.Add(ToEntity(transition));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw new SagaConcurrencyException(saga.TransferId, expectedVersion);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            throw new SagaConcurrencyException(saga.TransferId, expectedVersion);
        }
    }

    public async Task<IReadOnlyCollection<TransferSaga>> GetExpiredAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var terminalStates = new[]
        {
            nameof(TransferSagaState.Completed),
            nameof(TransferSagaState.Compensated),
            nameof(TransferSagaState.Failed),
            nameof(TransferSagaState.ManualIntervention)
        };
        var entities = await dbContext.TransferSagas.AsNoTracking()
            .Where(item => item.DeadlineAt <= now && !terminalStates.Contains(item.State))
            .OrderBy(item => item.DeadlineAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyCollection<SagaTransition>> GetTransitionsAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.SagaTransitions.AsNoTracking()
            .Where(item => item.TransferId == transferId)
            .OrderBy(item => item.NextVersion)
            .ToArrayAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    private static TransferSagaEntity ToEntity(TransferSaga saga) => new()
    {
        TransferId = saga.TransferId,
        IdempotencyKey = saga.IdempotencyKey,
        SenderUserId = saga.SenderUserId,
        SenderAccountId = saga.SenderAccountId,
        SenderBankId = saga.SenderBankId,
        RecipientUserId = saga.RecipientUserId,
        RecipientAccountId = saga.RecipientAccountId,
        RecipientBankId = saga.RecipientBankId,
        Amount = saga.Amount.Value,
        SimulationMode = saga.SimulationMode.ToString(),
        State = saga.State.ToString(),
        Version = saga.Version,
        FailureCode = saga.FailureCode,
        FailureReason = saga.FailureReason,
        CreatedAt = saga.CreatedAt,
        UpdatedAt = saga.UpdatedAt,
        DeadlineAt = saga.DeadlineAt,
        CompletedAt = saga.CompletedAt,
        CompensationStartedAt = saga.CompensationStartedAt,
        CompensatedAt = saga.CompensatedAt
    };

    private static SagaTransitionEntity ToEntity(SagaTransition transition) => new()
    {
        TransitionId = transition.TransitionId,
        TransferId = transition.TransferId,
        PreviousState = transition.PreviousState?.ToString(),
        NextState = transition.NextState.ToString(),
        PreviousVersion = transition.PreviousVersion,
        NextVersion = transition.NextVersion,
        TriggeringMessageId = transition.TriggeringMessageId,
        TriggeringMessageType = transition.TriggeringMessageType,
        CorrelationId = transition.CorrelationId,
        CausationId = transition.CausationId,
        Reason = transition.Reason,
        RecordedAt = transition.RecordedAt
    };

    private static TransferSaga ToDomain(TransferSagaEntity entity) =>
        TransferSaga.Rehydrate(
            entity.TransferId,
            entity.IdempotencyKey,
            entity.SenderUserId,
            entity.SenderAccountId,
            entity.SenderBankId,
            entity.RecipientUserId,
            entity.RecipientAccountId,
            entity.RecipientBankId,
            entity.Amount,
            Enum.Parse<TransferSimulationMode>(entity.SimulationMode),
            Enum.Parse<TransferSagaState>(entity.State),
            entity.Version,
            entity.FailureCode,
            entity.FailureReason,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.DeadlineAt,
            entity.CompletedAt,
            entity.CompensationStartedAt,
            entity.CompensatedAt);

    private static SagaTransition ToDomain(SagaTransitionEntity entity) => new(
        entity.TransitionId,
        entity.TransferId,
        entity.PreviousState is null ? null : Enum.Parse<TransferSagaState>(entity.PreviousState),
        Enum.Parse<TransferSagaState>(entity.NextState),
        entity.PreviousVersion,
        entity.NextVersion,
        entity.TriggeringMessageId,
        entity.TriggeringMessageType,
        entity.CorrelationId,
        entity.CausationId,
        entity.Reason,
        entity.RecordedAt);
}
