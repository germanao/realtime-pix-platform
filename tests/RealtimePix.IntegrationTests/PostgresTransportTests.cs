using Microsoft.Extensions.Options;
using Npgsql;
using RealtimePix.Eventing;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgresTransportTests(PostgreSqlFixture postgres)
{
    [IntegrationFact]
    public async Task Publish_is_deduplicated_and_acknowledgements_are_per_consumer()
    {
        var connection = await CreateBusDatabaseAsync();
        await using var first = CreateBus(connection, "first");
        await using var second = CreateBus(connection, "second");
        await first.PublishAsync("Test.v1", 1, "test", new { value = 1 });
        var envelope = Assert.Single(await first.ReadPendingAsync(default));
        await first.PublishEnvelopeAsync(envelope);
        Assert.Single(await first.ReadPendingAsync(default));
        await first.AcknowledgeAsync(envelope.EventId, default);
        Assert.Empty(await first.ReadPendingAsync(default));
        Assert.Single(await second.ReadPendingAsync(default));
        Assert.True((await first.CheckAsync(default)).IsReady);
    }

    [IntegrationFact]
    public async Task Commands_only_reach_the_destination_and_unacknowledged_messages_survive_restart()
    {
        var connection = await CreateBusDatabaseAsync();
        await using (var sender = CreateBus(connection, "sender"))
            await sender.PublishCommandAsync("bank-a-commands", "Debit.v1", 1, "test", new { value = 25 });
        await using var wrongBank = CreateBus(connection, "bank-b", "bank-b-commands");
        Assert.Empty(await wrongBank.ReadPendingAsync(default));
        Guid messageId;
        await using (var bank = CreateBus(connection, "bank-a", "bank-a-commands"))
            messageId = Assert.Single(await bank.ReadPendingAsync(default)).EventId;
        await using var restarted = CreateBus(connection, "bank-a", "bank-a-commands");
        Assert.Equal(messageId, Assert.Single(await restarted.ReadPendingAsync(default)).EventId);
        await restarted.AcknowledgeAsync(messageId, default);
        Assert.Empty(await restarted.ReadPendingAsync(default));
    }

    [IntegrationFact]
    public async Task A_late_commit_is_not_lost_after_a_newer_message_is_acknowledged()
    {
        var connection = await CreateBusDatabaseAsync();
        await using var bus = CreateBus(connection, "consumer");
        await bus.PublishAsync("Template.v1", 1, "test", new { });
        var template = Assert.Single(await bus.ReadPendingAsync(default));
        await bus.AcknowledgeAsync(template.EventId, default);
        await using var delayed = new NpgsqlConnection(connection);
        await delayed.OpenAsync();
        await using var transaction = await delayed.BeginTransactionAsync();
        var late = template with { EventId = Guid.NewGuid() };
        await using (var insert = new NpgsqlCommand("INSERT INTO bus_messages(id,kind,destination,envelope) VALUES($1,'Event','platform-events',$2::jsonb)", delayed, transaction))
        {
            insert.Parameters.AddWithValue(late.EventId);
            insert.Parameters.AddWithValue(System.Text.Json.JsonSerializer.Serialize(late, JsonDefaults.Options));
            await insert.ExecuteNonQueryAsync();
        }
        await bus.PublishAsync("Newer.v1", 1, "test", new { });
        var newer = Assert.Single(await bus.ReadPendingAsync(default));
        await bus.AcknowledgeAsync(newer.EventId, default);
        await transaction.CommitAsync();
        Assert.Equal(late.EventId, Assert.Single(await bus.ReadPendingAsync(default)).EventId);
    }

    private async Task<string> CreateBusDatabaseAsync()
    {
        var connection = await postgres.CreateDatabaseAsync("transport");
        await using var source = NpgsqlDataSource.Create(connection);
        await using var command = source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "bus-schema.sql")));
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static PostgresEventBus CreateBus(string connection, string consumer, string? queue = null) =>
        new(Options.Create(new PostgresEventBusOptions { ConnectionString = connection, ConsumerName = consumer, QueueName = queue }));
}
