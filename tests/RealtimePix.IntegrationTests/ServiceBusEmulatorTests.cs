using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RealtimePix.Eventing;
using Testcontainers.ServiceBus;
using Xunit;

namespace RealtimePix.IntegrationTests;

public sealed class ServiceBusEmulatorTests
{
    private const string EmulatorImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:1.1.2-amd64";
    private const string QueueName = "queue.1";
    private const string MessageType = "IntegrationProbe.v1";

    [IntegrationFact]
    public async Task Local_event_bus_routes_matching_command_and_redelivers_after_failure()
    {
        await using var container = new ServiceBusBuilder(EmulatorImage)
            .WithAcceptLicenseAgreement(true)
            .Build();
        await container.StartAsync();
        await using var client = new ServiceBusClient(container.GetConnectionString());
        var options = Options.Create(new ServiceBusEventBusOptions
        {
            ConnectionString = container.GetConnectionString(),
            QueueName = QueueName,
            MaxConcurrentCalls = 1
        });
        var handler = new FailOnceHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler>(handler);
        await using var provider = services.BuildServiceProvider();
        var worker = new ServiceBusEventBusWorker(
            client,
            options,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ServiceBusEventBusWorker>.Instance);
        await using var publisher = new ServiceBusIntegrationEventPublisher(
            client,
            options,
            NullLogger<ServiceBusIntegrationEventPublisher>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await publisher.PublishCommandAsync(
                QueueName,
                MessageType,
                1,
                "integration-tests",
                new { value = 42 },
                correlationId: "probe-correlation",
                cancellationToken: CancellationToken.None);

            var received = await handler.Processed.Task.WaitAsync(TimeSpan.FromSeconds(45));
            Assert.Equal(MessageType, received.EventType);
            Assert.Equal("probe-correlation", received.CorrelationId);
            Assert.Equal(2, handler.Attempts);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private sealed class FailOnceHandler : IIntegrationEventHandler
    {
        private int _attempts;

        public IReadOnlyCollection<string> EventTypes { get; } = [MessageType];

        public int Attempts => _attempts;

        public TaskCompletionSource<EventEnvelope> Processed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("Simulated handler crash after delivery.");
            }

            Processed.TrySetResult(envelope);
            return Task.CompletedTask;
        }
    }
}
