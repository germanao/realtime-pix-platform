using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class FileEventBusWorker(
    IOptions<FileEventBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<FileEventBusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var busOptions = options.Value;
        System.IO.Directory.CreateDirectory(busOptions.Directory);
        System.IO.Directory.CreateDirectory(Path.Combine(busOptions.Directory, "offsets"));

        var eventsPath = Path.Combine(busOptions.Directory, "events.jsonl");
        var offsetPath = Path.Combine(busOptions.Directory, "offsets", $"{busOptions.ConsumerName}.offset");
        var handledLines = ReadOffset(offsetPath);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(eventsPath))
                {
                    var lines = await File.ReadAllLinesAsync(eventsPath, stoppingToken);
                    for (var index = handledLines; index < lines.Length; index++)
                    {
                        handledLines = await HandleLineAsync(lines[index], index, stoppingToken);
                        await File.WriteAllTextAsync(offsetPath, handledLines.ToString(), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local event bus polling failed for consumer {ConsumerName}.", busOptions.ConsumerName);
            }

            await Task.Delay(busOptions.PollIntervalMilliseconds, stoppingToken);
        }
    }

    private async Task<int> HandleLineAsync(
        string line,
        int index,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return index + 1;
        }

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(line, JsonDefaults.Options);
        if (envelope is null)
        {
            return index + 1;
        }

        var optionsValue = options.Value;
        var isCommand = envelope.MessageKind.Equals(
            IntegrationMessageKind.Command.ToString(),
            StringComparison.OrdinalIgnoreCase);
        if (isCommand && !string.Equals(
                envelope.Destination,
                optionsValue.QueueName,
                StringComparison.OrdinalIgnoreCase))
        {
            return index + 1;
        }

        using var scope = scopeFactory.CreateScope();
        var inbox = scope.ServiceProvider.GetService<IIntegrationInbox>();
        if (inbox is not null && !await inbox.TryBeginProcessingAsync(envelope, cancellationToken))
        {
            return index + 1;
        }

        try
        {
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler>();
            foreach (var handler in handlers.Where(handler => handler.EventTypes.Contains(envelope.EventType)))
            {
                await handler.HandleAsync(envelope, cancellationToken);
            }

            if (inbox is not null)
            {
                await inbox.MarkProcessedAsync(envelope, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (inbox is not null)
            {
                await inbox.MarkFailedAsync(envelope, ex, cancellationToken);
            }

            throw;
        }

        return index + 1;
    }

    private static int ReadOffset(string offsetPath)
    {
        if (!File.Exists(offsetPath))
        {
            return 0;
        }

        var value = File.ReadAllText(offsetPath);
        return int.TryParse(value, out var offset) ? offset : 0;
    }
}
