using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class FileEventBusWorkerTests
{
    [Fact]
    public async Task Local_event_bus_delivers_to_matching_handlers()
    {
        var directory = CreateBusDirectory();
        try
        {
            var options = Options.Create(new FileEventBusOptions
            {
                Directory = directory,
                ConsumerName = "consumer-a",
                PollIntervalMilliseconds = 25
            });
            var handler = new RecordingHandler(EventTypes.UserPresenceChanged);
            using var worker = CreateWorker(options, handler);
            await worker.StartAsync(CancellationToken.None);
            var publisher = new FileIntegrationEventPublisher(options, new SilentLogger<FileIntegrationEventPublisher>());

            await publisher.PublishAsync(
                EventTypes.UserPresenceChanged,
                1,
                "identity-presence-service",
                new UserPresenceChangedPayload("user-a", "Azure Ledger", true, false, DateTimeOffset.UtcNow),
                correlationId: "user-a",
                cancellationToken: CancellationToken.None);

            await WaitUntilAsync(() => handler.Envelopes.Count == 1);
            await worker.StopAsync(CancellationToken.None);

            var envelope = Assert.Single(handler.Envelopes);
            Assert.Equal(EventTypes.UserPresenceChanged, envelope.EventType);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Handler_offset_prevents_replay_after_restart()
    {
        var directory = CreateBusDirectory();
        try
        {
            var options = Options.Create(new FileEventBusOptions
            {
                Directory = directory,
                ConsumerName = "consumer-a",
                PollIntervalMilliseconds = 25
            });
            var firstHandler = new RecordingHandler(EventTypes.UserPresenceChanged);
            using (var firstWorker = CreateWorker(options, firstHandler))
            {
                await firstWorker.StartAsync(CancellationToken.None);
                var publisher = new FileIntegrationEventPublisher(options, new SilentLogger<FileIntegrationEventPublisher>());
                await publisher.PublishAsync(
                    EventTypes.UserPresenceChanged,
                    1,
                    "identity-presence-service",
                    new UserPresenceChangedPayload("user-a", "Azure Ledger", true, false, DateTimeOffset.UtcNow),
                    correlationId: "user-a",
                    cancellationToken: CancellationToken.None);
                await WaitUntilAsync(() => firstHandler.Envelopes.Count == 1);
                await firstWorker.StopAsync(CancellationToken.None);
            }

            var secondHandler = new RecordingHandler(EventTypes.UserPresenceChanged);
            using (var secondWorker = CreateWorker(options, secondHandler))
            {
                await secondWorker.StartAsync(CancellationToken.None);
                await Task.Delay(150);
                await secondWorker.StopAsync(CancellationToken.None);
            }

            Assert.Empty(secondHandler.Envelopes);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Concurrent_publish_writes_every_event_once()
    {
        var directory = CreateBusDirectory();
        try
        {
            var options = Options.Create(new FileEventBusOptions
            {
                Directory = directory,
                ConsumerName = "publisher-test",
                PollIntervalMilliseconds = 25
            });
            var publisher = new FileIntegrationEventPublisher(options, new SilentLogger<FileIntegrationEventPublisher>());

            var tasks = Enumerable.Range(0, 40)
                .Select(index => publisher.PublishAsync(
                    EventTypes.UserPresenceChanged,
                    1,
                    "identity-presence-service",
                    new UserPresenceChangedPayload($"user-{index}", $"User {index}", true, false, DateTimeOffset.UtcNow),
                    correlationId: $"user-{index}",
                    cancellationToken: CancellationToken.None))
                .ToArray();

            await Task.WhenAll(tasks);

            var lines = await File.ReadAllLinesAsync(Path.Combine(directory, "events.jsonl"));
            Assert.Equal(40, lines.Length);
            Assert.All(lines, line => Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<EventEnvelope>(line, JsonDefaults.Options)));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static FileEventBusWorker CreateWorker(IOptions<FileEventBusOptions> options, IIntegrationEventHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var serviceProvider = services.BuildServiceProvider();
        return new FileEventBusWorker(
            options,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new SilentLogger<FileEventBusWorker>());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private static string CreateBusDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "realtime-pix-eventing-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingHandler(params string[] eventTypes) : IIntegrationEventHandler
    {
        public IReadOnlyCollection<string> EventTypes { get; } = eventTypes;

        public List<EventEnvelope> Envelopes { get; } = [];

        public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
