using System.Text.Json;
using RealtimePix.Contracts;

var root = FindRepositoryRoot();

Require(File.Exists(Path.Combine(root, "Directory.Build.props")), "Directory.Build.props must exist.");
Require(Directory.GetDirectories(Path.Combine(root, "services")).Length >= 6, "At least six backend services must exist.");

var eventCatalogPath = Path.Combine(root, "contracts", "events", "event-catalog.json");
using var catalogStream = File.OpenRead(eventCatalogPath);
var catalog = JsonSerializer.Deserialize<EventCatalog>(catalogStream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("Event catalog could not be read.");

var contractEvents = EventTypes.All.Order(StringComparer.OrdinalIgnoreCase).ToArray();
var catalogEvents = catalog.Events.Order(StringComparer.OrdinalIgnoreCase).ToArray();

Require(contractEvents.SequenceEqual(catalogEvents), "Event catalog must match .NET EventTypes.All.");

var postgresScripts = new[]
{
    "identity_presence.sql",
    "wallet_ledger.sql",
    "transaction.sql"
};

foreach (var script in postgresScripts)
{
    Require(File.Exists(Path.Combine(root, "infra", "postgres", script)), $"Missing PostgreSQL ownership script: {script}");
}

Console.WriteLine("Architecture checks passed.");

static string FindRepositoryRoot()
{
    var current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (File.Exists(Path.Combine(current, "Directory.Build.props")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName ?? string.Empty;
    }

    throw new InvalidOperationException("Repository root could not be located.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

public sealed record EventCatalog(string Schema, string[] Events);

