using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class ServiceBusEventBusWorker(
    ServiceBusClient client,
    IOptions<ServiceBusEventBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceBusEventBusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

        processor.ProcessMessageAsync += message => ProcessMessageAsync(message, stoppingToken);
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
        CancellationToken cancellationToken)
    {
        EventEnvelope? envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(args.Message.Body, JsonDefaults.Options);
            if (envelope is null)
            {
                await args.DeadLetterMessageAsync(args.Message, "InvalidEnvelope", "The message body could not be deserialized as an event envelope.", cancellationToken);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler>().ToArray();
            if (handlers.Length == 0)
            {
                await args.CompleteMessageAsync(args.Message, cancellationToken);
                return;
            }

            var inbox = scope.ServiceProvider.GetService<IIntegrationInbox>();
            if (inbox is not null && !await inbox.TryBeginProcessingAsync(envelope, cancellationToken))
            {
                await args.CompleteMessageAsync(args.Message, cancellationToken);
                return;
            }

            foreach (var handler in handlers.Where(handler => handler.EventTypes.Contains(envelope.EventType)))
            {
                await handler.HandleAsync(envelope, cancellationToken);
            }

            if (inbox is not null)
            {
                await inbox.MarkProcessedAsync(envelope, cancellationToken);
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
            if (envelope is not null)
            {
                using var failureScope = scopeFactory.CreateScope();
                var inbox = failureScope.ServiceProvider.GetService<IIntegrationInbox>();
                if (inbox is not null)
                {
                    await inbox.MarkFailedAsync(envelope, ex, cancellationToken);
                }
            }

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
