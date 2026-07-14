mock_provider "azurerm" {}

mock_provider "random" {
  mock_resource "random_id" {
    defaults = {
      hex = "a1b2c3d4"
    }
  }
}

run "production_reference_security_contract" {
  command = plan

  variables {
    tenant_id             = "00000000-0000-0000-0000-000000000000"
    entra_admin_object_id = "00000000-0000-0000-0000-000000000001"
    entra_admin_name      = "migration-identity"
    publisher_email       = "operations@example.invalid"
    allowed_browser_origins = [
      "https://realtime-pix.example"
    ]
  }

  assert {
    condition     = toset(keys(module.postgresql)) == toset(["identity", "bank_a", "bank_b", "transaction", "realtime"])
    error_message = "Production must isolate the five state owners on separate PostgreSQL module instances."
  }

  assert {
    condition     = azurerm_container_registry.this.sku == "Premium" && !azurerm_container_registry.this.public_network_access_enabled
    error_message = "Production ACR must use Premium with its public endpoint disabled."
  }

  assert {
    condition     = azurerm_container_app_environment.this.internal_load_balancer_enabled
    error_message = "The production Container Apps environment must be internal."
  }

  assert {
    condition     = length(module.workload_identity) == 7 && length(module.stateful_app) == 5
    error_message = "Production must model seven dedicated identities and all five stateful service apps."
  }

  assert {
    condition     = one(azurerm_container_app_environment.this.workload_profile).workload_profile_type == "D4"
    error_message = "Production workloads must use the dedicated D4 workload profile."
  }

  assert {
    condition     = module.api_gateway.name != "" && module.bot.name != ""
    error_message = "Production must include the Gateway and Bot deployments in addition to stateful services."
  }

  assert {
    condition     = length(azurerm_api_management_api.realtime_hub) == 2
    error_message = "APIM must expose both Azure SignalR negotiation endpoints."
  }

  assert {
    condition     = length(module.diagnostic_setting) == 11
    error_message = "Every production PaaS/data service must emit Azure Monitor diagnostics."
  }

  assert {
    condition = (
      azurerm_key_vault.this.purge_protection_enabled &&
      !azurerm_key_vault.this.public_network_access_enabled &&
      one(azurerm_key_vault.this.network_acls).default_action == "Deny"
    )
    error_message = "Production Key Vault must use purge protection, private access, and a deny-by-default firewall."
  }
}
