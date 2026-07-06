using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class ServiceBusEventBusWorker(
    ServiceBusClient client,
    IOptions<ServiceBusEventBusOptions> options,
    IEnumerable<IIntegrationEventHandler> handlers,
    ILogger<ServiceBusEventBusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var handlerList = handlers.ToArray();
        if (handlerList.Length == 0)
        {
            logger.LogInformation("Service Bus worker skipped because no handlers are registered.");
            return;
        }

        var busOptions = options.Value;
        if (string.IsNullOrWhiteSpace(busOptions.SubscriptionName))
        {
            throw new InvalidOperationException("Service Bus consumer requires EventBus:ServiceBus:SubscriptionName.");
        }

        await using var processor = client.CreateProcessor(
            busOptions.TopicName,
            busOptions.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, busOptions.MaxConcurrentCalls)
            });

        processor.ProcessMessageAsync += message => ProcessMessageAsync(message, handlerList, stoppingToken);
        processor.ProcessErrorAsync += ProcessErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Service Bus worker started for {Topic}/{Subscription}.", busOptions.TopicName, busOptions.SubscriptionName);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task ProcessMessageAsync(
        ProcessMessageEventArgs args,
        IReadOnlyCollection<IIntegrationEventHandler> handlers,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(args.Message.Body, JsonDefaults.Options);
            if (envelope is null)
            {
                await args.DeadLetterMessageAsync(args.Message, "InvalidEnvelope", "The message body could not be deserialized as an event envelope.", cancellationToken);
                return;
            }

            foreach (var handler in handlers.Where(handler => handler.EventTypes.Contains(envelope.EventType)))
            {
                await handler.HandleAsync(envelope, cancellationToken);
            }

            await args.CompleteMessageAsync(args.Message, cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Service Bus message {MessageId} contains invalid JSON.", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "InvalidJson", ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Service Bus message {MessageId} failed and will be retried.", args.Message.MessageId);
            throw;
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Service Bus processing error from {ErrorSource} for {EntityPath}.", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
