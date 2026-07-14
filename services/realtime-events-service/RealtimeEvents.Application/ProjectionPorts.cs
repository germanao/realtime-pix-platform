namespace RealtimeEvents.Application;

public interface IRealtimeProjectionStore
{
    Task<bool> TryAddTimelineAsync(TimelineEventResponse item, CancellationToken cancellationToken);

    Task<bool> TryAddFlowStepAsync(string sourceEventId, FlowStepResponse step, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TimelineEventResponse>> GetTimelineAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FlowStepResponse>> GetFlowAsync(string transferId, CancellationToken cancellationToken);
}

public interface IRealtimeProjectionNotifier
{
    Task TimelineItemAsync(TimelineEventResponse item, CancellationToken cancellationToken);

    Task TransferFlowStepAsync(FlowStepResponse step, CancellationToken cancellationToken);
}

public interface IArchitectureFlowPublisher
{
    Task PublishAsync(FlowStepResponse step, PlatformEvent source, CancellationToken cancellationToken);
}

public interface IRealtimeProjectionTransaction
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public interface IRealtimeEventsReadinessProbe
{
    Task<RealtimeEventsReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public interface IRealtimeTransportReadinessProbe
{
    Task<RealtimeTransportReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
