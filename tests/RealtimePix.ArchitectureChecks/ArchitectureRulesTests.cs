using System.Text.Json;
using System.Xml.Linq;
using RealtimePix.Contracts;
using Xunit;

namespace RealtimePix.ArchitectureChecks;

public sealed class ArchitectureRulesTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string, string> CleanArchitectureServices => new()
    {
        { "services/api-gateway", "ApiGateway" },
        { "services/identity-presence-service", "IdentityPresence" },
        { "services/bank-ledger-service", "BankLedger" },
        { "services/transaction-service", "Transaction" },
        { "services/realtime-events-service", "RealtimeEvents" },
        { "services/bot-service", "Bot" }
    };

    [Theory]
    [MemberData(nameof(CleanArchitectureServices))]
    public void Service_projects_follow_clean_architecture_dependencies(string servicePath, string projectPrefix)
    {
        var serviceRoot = Path.Combine(Root, servicePath);
        var domain = LoadProject(serviceRoot, projectPrefix, "Domain");
        var application = LoadProject(serviceRoot, projectPrefix, "Application");
        var infrastructure = LoadProject(serviceRoot, projectPrefix, "Infrastructure");
        var hostSuffix = projectPrefix == "Bot" ? "Worker" : "Api";
        var host = LoadProject(serviceRoot, projectPrefix, hostSuffix);

        Assert.Empty(ProjectReferences(domain));
        Assert.Contains(ProjectReferences(application), reference => reference.EndsWith($"{projectPrefix}.Domain.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(ProjectReferences(application), reference => reference.Contains("Infrastructure", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(infrastructure), reference => reference.EndsWith($"{projectPrefix}.Application.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(host), reference => reference.EndsWith($"{projectPrefix}.Application.csproj", StringComparison.Ordinal));
        Assert.Contains(ProjectReferences(host), reference => reference.EndsWith($"{projectPrefix}.Infrastructure.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Ef_core_and_migrations_are_owned_by_infrastructure_projects()
    {
        var activeServices = CleanArchitectureServices
            .Select(item => Path.Combine(Root, item[0].ToString()!))
            .ToArray();

        foreach (var serviceRoot in activeServices)
        {
            foreach (var projectPath in Directory.GetFiles(serviceRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var project = XDocument.Load(projectPath);
                var efPackages = project.Descendants("PackageReference")
                    .Select(element => element.Attribute("Include")?.Value)
                    .Where(package => package?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
                    .ToArray();

                if (efPackages.Length > 0)
                {
                    Assert.Contains(".Infrastructure", Path.GetFileNameWithoutExtension(projectPath), StringComparison.Ordinal);
                }
            }

            foreach (var migration in Directory.GetFiles(serviceRoot, "*Migration*.cs", SearchOption.AllDirectories))
            {
                Assert.Contains("Infrastructure", migration, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Integration_event_catalog_matches_dotnet_contracts()
    {
        var eventCatalogPath = Path.Combine(Root, "contracts", "events", "event-catalog.json");
        using var catalogStream = File.OpenRead(eventCatalogPath);
        var catalog = JsonSerializer.Deserialize<EventCatalog>(catalogStream, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(catalog);
        Assert.Equal(
            EventTypes.All.Order(StringComparer.OrdinalIgnoreCase),
            catalog.Events.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Each_state_owner_has_a_database_bootstrap_script()
    {
        var expectedScripts = new[]
        {
            "identity_presence.sql",
            "bank_a_ledger.sql",
            "bank_b_ledger.sql",
            "transaction.sql",
            "realtime_projection.sql"
        };

        foreach (var script in expectedScripts)
        {
            Assert.True(File.Exists(Path.Combine(Root, "infra", "postgres", script)), $"Missing PostgreSQL ownership script: {script}");
        }
    }

    [Fact]
    public void Domain_projects_do_not_reference_framework_or_integration_contracts()
    {
        foreach (var item in CleanArchitectureServices)
        {
            var project = LoadProject(Path.Combine(Root, item[0].ToString()!), item[1].ToString()!, "Domain");
            Assert.Empty(project.Descendants("PackageReference"));
            Assert.Empty(project.Descendants("ProjectReference"));
        }
    }

    private static XDocument LoadProject(string serviceRoot, string prefix, string suffix)
    {
        var path = Path.Combine(serviceRoot, $"{prefix}.{suffix}", $"{prefix}.{suffix}.csproj");
        Assert.True(File.Exists(path), $"Missing required layer project: {path}");
        return XDocument.Load(path);
    }

    private static string[] ProjectReferences(XDocument project)
    {
        return project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
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

    private sealed record EventCatalog(string Schema, string[] Events);
}
