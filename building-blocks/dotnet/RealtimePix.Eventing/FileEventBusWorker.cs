using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class FileEventBusWorker(
    IOptions<FileEventBusOptions> options,
    IEnumerable<IIntegrationEventHandler> handlers,
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
        var handlerList = handlers.ToArray();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(eventsPath))
                {
                    var lines = await File.ReadAllLinesAsync(eventsPath, stoppingToken);
                    for (var index = handledLines; index < lines.Length; index++)
                    {
                        handledLines = await HandleLineAsync(lines[index], index, handlerList, stoppingToken);
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
        IReadOnlyCollection<IIntegrationEventHandler> handlers,
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

        foreach (var handler in handlers.Where(handler => handler.EventTypes.Contains(envelope.EventType)))
        {
            await handler.HandleAsync(envelope, cancellationToken);
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

