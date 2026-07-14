using RealtimeEvents.Application;

namespace RealtimeEvents.Infrastructure;

public sealed class InMemoryRealtimeProjectionStore : IRealtimeProjectionStore
{
    private readonly object _gate = new();
    private readonly List<TimelineEventResponse> _timeline = [];
    private readonly Dictionary<string, List<FlowStepResponse>> _flowByTransfer = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _timelineEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flowSourceEventIds = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> TryAddTimelineAsync(TimelineEventResponse item, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_timelineEventIds.Add(item.EventId))
            {
                return Task.FromResult(false);
            }

            _timeline.Add(item);
            if (_timeline.Count > 250)
            {
                _timelineEventIds.Remove(_timeline[0].EventId);
                _timeline.RemoveAt(0);
            }

            return Task.FromResult(true);
        }
    }

    public Task<bool> TryAddFlowStepAsync(string sourceEventId, FlowStepResponse step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.TransferId))
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            if (!_flowSourceEventIds.Add(sourceEventId))
            {
                return Task.FromResult(false);
            }

            if (!_flowByTransfer.TryGetValue(step.TransferId, out var steps))
            {
                steps = [];
                _flowByTransfer[step.TransferId] = steps;
            }

            steps.Add(step);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyCollection<TimelineEventResponse>> GetTimelineAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<TimelineEventResponse>>(
                _timeline.OrderByDescending(item => item.OccurredAt).ToArray());
        }
    }

    public Task<IReadOnlyCollection<FlowStepResponse>> GetFlowAsync(string transferId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyCollection<FlowStepResponse> result = _flowByTransfer.TryGetValue(transferId, out var steps)
                ? steps.OrderBy(step => step.RecordedAt).ToArray()
                : [];
            return Task.FromResult(result);
        }
    }
}
