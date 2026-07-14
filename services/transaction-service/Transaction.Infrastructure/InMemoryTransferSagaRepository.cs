using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class InMemoryTransferSagaRepository : ITransferSagaRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TransferSaga> _sagas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idempotency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SagaTransition>> _transitions = new(StringComparer.OrdinalIgnoreCase);

    public Task AddAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_idempotency.ContainsKey(saga.IdempotencyKey))
            {
                throw new DuplicateIdempotencyKeyException(saga.IdempotencyKey);
            }

            _sagas.Add(saga.TransferId, Clone(saga));
            _idempotency.Add(saga.IdempotencyKey, saga.TransferId);
            _transitions.Add(saga.TransferId, [transition]);
            return Task.CompletedTask;
        }
    }

    public Task<TransferSaga?> GetAsync(string transferId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_sagas.TryGetValue(transferId, out var saga) ? Clone(saga) : null);
        }
    }

    public Task<TransferSaga?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_idempotency.TryGetValue(idempotencyKey, out var transferId))
            {
                return Task.FromResult<TransferSaga?>(null);
            }

            return Task.FromResult<TransferSaga?>(Clone(_sagas[transferId]));
        }
    }

    public Task SaveTransitionAsync(
        TransferSaga saga,
        int expectedVersion,
        SagaTransition transition,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_sagas.TryGetValue(saga.TransferId, out var existing) || existing.Version != expectedVersion)
            {
                throw new SagaConcurrencyException(saga.TransferId, expectedVersion);
            }

            _sagas[saga.TransferId] = Clone(saga);
            _transitions[saga.TransferId].Add(transition);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyCollection<TransferSaga>> GetExpiredAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyCollection<TransferSaga> result = _sagas.Values
                .Where(item => !item.IsTerminal && item.DeadlineAt <= now)
                .OrderBy(item => item.DeadlineAt)
                .Take(take)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyCollection<SagaTransition>> GetTransitionsAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyCollection<SagaTransition> result = _transitions.TryGetValue(transferId, out var transitions)
                ? transitions.OrderBy(item => item.NextVersion).ToArray()
                : [];
            return Task.FromResult(result);
        }
    }

    private static TransferSaga Clone(TransferSaga saga) =>
        TransferSaga.Rehydrate(
            saga.TransferId,
            saga.IdempotencyKey,
            saga.SenderUserId,
            saga.SenderAccountId,
            saga.SenderBankId,
            saga.RecipientUserId,
            saga.RecipientAccountId,
            saga.RecipientBankId,
            saga.Amount.Value,
            saga.SimulationMode,
            saga.State,
            saga.Version,
            saga.FailureCode,
            saga.FailureReason,
            saga.CreatedAt,
            saga.UpdatedAt,
            saga.DeadlineAt,
            saga.CompletedAt,
            saga.CompensationStartedAt,
            saga.CompensatedAt);
}
