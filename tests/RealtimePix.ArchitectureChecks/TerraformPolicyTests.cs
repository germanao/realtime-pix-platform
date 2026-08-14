using Xunit;

namespace RealtimePix.ArchitectureChecks;

public sealed class TerraformPolicyTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Production_reference_has_private_data_services_and_separate_databases()
    {
        var production = Read("infra", "terraform", "production-reference", "main.tf");

        Assert.Contains("sku                           = \"Premium\"", production, StringComparison.Ordinal);
        Assert.Contains("public_network_access_enabled = false", production, StringComparison.Ordinal);
        Assert.Contains("purge_protection_enabled      = true", production, StringComparison.Ordinal);
        Assert.Contains("for_each = local.databases", production, StringComparison.Ordinal);
        Assert.Matches(@"public_network_access\s*=\s*false", production);
        Assert.Contains("high_availability_mode = \"ZoneRedundant\"", production, StringComparison.Ordinal);
        Assert.Contains("workload_profile_type = \"D4\"", production, StringComparison.Ordinal);
        Assert.Contains("module \"workload_identity\"", production, StringComparison.Ordinal);
        Assert.Contains("module \"stateful_app\"", production, StringComparison.Ordinal);
        Assert.Contains("module \"api_gateway\"", production, StringComparison.Ordinal);
        Assert.Contains("module \"bot\"", production, StringComparison.Ordinal);
        Assert.Contains("Saga__AllowFailureSimulation", production, StringComparison.Ordinal);
        Assert.Contains("value = \"false\"", production, StringComparison.Ordinal);

        foreach (var owner in new[] { "identity", "bank_a", "bank_b", "transaction", "realtime" })
        {
            Assert.Contains(owner, production, StringComparison.Ordinal);
        }

        var apim = Read("infra", "terraform", "production-reference", "apim.tf");
        Assert.Contains("azurerm_api_management_api\" \"realtime_hub", apim, StringComparison.Ordinal);
        Assert.Contains("terminate-unmatched-request=\"true\"", apim, StringComparison.Ordinal);
    }

    [Fact]
    public void Poc_uses_dedicated_identities_and_scales_active_apps_to_zero()
    {
        var runtime = Read("infra", "terraform", "runtime", "main.tf");

        Assert.Contains("module \"workload_identity\"", runtime, StringComparison.Ordinal);
        Assert.Equal(7, CountOccurrences(runtime, "min_replicas = 0"));
        Assert.Equal(4, CountOccurrences(runtime, "custom_rule_type = \"azure-servicebus\""));
        Assert.Contains("resource \"azurerm_container_app_job\" \"bot\"", runtime, StringComparison.Ordinal);
        Assert.Contains("Bot__RunOnce", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("min_replicas = 1", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Poc_free_tier_guards_cap_telemetry_and_use_public_ghcr()
    {
        var foundation = Read("infra", "terraform", "foundation", "main.tf");
        var observability = Read("infra", "terraform", "modules", "observability", "main.tf");
        var runtimeVariables = Read("infra", "terraform", "runtime", "variables.tf");
        var deployment = Read(".github", "workflows", "deploy-poc.yml");

        Assert.Contains("daily_quota_gb      = 0.1", foundation, StringComparison.Ordinal);
        Assert.Contains("log_alerts_enabled  = false", foundation, StringComparison.Ordinal);
        Assert.Contains("daily_quota_gb      = var.daily_quota_gb", observability, StringComparison.Ordinal);
        Assert.Contains("default     = \"ghcr.io/germanao/realtime-pix\"", runtimeVariables, StringComparison.Ordinal);
        Assert.Contains("packages: write", deployment, StringComparison.Ordinal);
        Assert.DoesNotContain("azurerm_container_registry", foundation, StringComparison.Ordinal);
    }

    [Fact]
    public void Transaction_identity_can_send_to_both_bank_command_queues()
    {
        var runtime = Read("infra", "terraform", "runtime", "main.tf");

        Assert.Matches(
            @"bank_a_commands_sender\s*=\s*\{[\s\S]*?bank-a-commands[\s\S]*?Azure Service Bus Data Sender",
            runtime);
        Assert.Matches(
            @"bank_b_commands_sender\s*=\s*\{[\s\S]*?bank-b-commands[\s\S]*?Azure Service Bus Data Sender",
            runtime);
    }

    [Fact]
    public void Poc_and_production_route_paas_diagnostics_to_log_analytics()
    {
        var foundation = Read("infra", "terraform", "foundation", "main.tf");
        var production = Read("infra", "terraform", "production-reference", "main.tf");
        var module = Read("infra", "terraform", "modules", "diagnostic-settings", "main.tf");

        Assert.Contains("module \"diagnostic_setting\"", foundation, StringComparison.Ordinal);
        Assert.Contains("module \"diagnostic_setting\"", production, StringComparison.Ordinal);
        Assert.Contains("azurerm_monitor_diagnostic_categories", module, StringComparison.Ordinal);
        Assert.Contains("log_analytics_destination_type = \"Dedicated\"", module, StringComparison.Ordinal);
    }

    [Fact]
    public void Poc_omits_unused_secret_and_push_services_while_production_key_vault_defaults_to_deny()
    {
        var foundation = Read("infra", "terraform", "foundation", "main.tf");
        var production = Read("infra", "terraform", "production-reference", "main.tf");

        Assert.DoesNotContain("azurerm_key_vault", foundation, StringComparison.Ordinal);
        Assert.DoesNotContain("azurerm_notification_hub", foundation, StringComparison.Ordinal);
        Assert.Matches(
            @"network_acls\s*\{\s*bypass\s*=\s*""None""\s*default_action\s*=\s*""Deny""",
            production);
    }

    [Fact]
    public void Service_bus_filters_never_use_a_default_true_filter()
    {
        var foundation = Read("infra", "terraform", "foundation", "main.tf");
        var module = Read("infra", "terraform", "modules", "service-bus-topology", "variables.tf");

        Assert.DoesNotContain("= \"1 = 1\"", foundation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrueFilter expressions are forbidden", module, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHub_apply_workflow_cannot_manage_its_own_bootstrap_identity()
    {
        var workflow = Read(".github", "workflows", "infrastructure-apply.yml");

        Assert.DoesNotContain("options: [bootstrap", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan or apply bootstrap", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("working-directory: infra/terraform/bootstrap", workflow, StringComparison.Ordinal);
        Assert.Contains("options: [foundation, runtime, all]", workflow, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("foundation")]
    [InlineData("runtime")]
    [InlineData("production-reference")]
    public void Root_stacks_commit_provider_locks(string stack)
    {
        Assert.True(File.Exists(Path.Combine(Root, "infra", "terraform", stack, ".terraform.lock.hcl")));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
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
}
