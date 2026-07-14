mock_provider "azurerm" {}

run "standard_saga_broker_contract" {
  command = plan

  module {
    source = "../modules/service-bus-topology"
  }

  variables {
    name                = "sb-realtime-pix-test"
    resource_group_name = "rg-test"
    location            = "brazilsouth"
    namespace = {
      sku                   = "Standard"
      public_network_access = true
    }
    topic = { name = "platform-events" }
    command_queues = {
      bank-a-commands = {}
      bank-b-commands = {}
    }
    subscriptions = {
      transaction = "eventType IN ('FundsDebited.v1')"
      realtime    = "eventType IN ('SagaTransitionRecorded.v1')"
    }
  }

  assert {
    condition     = azurerm_servicebus_namespace.this.local_auth_enabled == false
    error_message = "Service Bus local authentication must remain disabled."
  }
  assert {
    condition     = length(azurerm_servicebus_queue.commands) == 2
    error_message = "The Saga requires one command queue per bank."
  }
}

run "private_entra_only_postgresql_contract" {
  command = plan

  module {
    source = "../modules/postgresql"
  }

  variables {
    name                = "pg-realtime-pix-test"
    resource_group_name = "rg-test"
    location            = "brazilsouth"
    tenant_id           = "00000000-0000-0000-0000-000000000000"
    administrator = {
      entra_object_id = "00000000-0000-0000-0000-000000000001"
      entra_name      = "migration-identity"
    }
    server = {
      sku_name               = "GP_Standard_D2ds_v5"
      storage_mb             = 131072
      backup_retention_days  = 35
      public_network_access  = false
      delegated_subnet_id    = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet/subnets/postgres"
      private_dns_zone_id    = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Network/privateDnsZones/test.postgres.database.azure.com"
      high_availability_mode = "ZoneRedundant"
    }
    databases = ["transaction_db"]
  }

  assert {
    condition     = azurerm_postgresql_flexible_server.this.authentication[0].password_auth_enabled == false
    error_message = "Password authentication must remain disabled."
  }
  assert {
    condition     = azurerm_postgresql_flexible_server.this.public_network_access_enabled == false
    error_message = "Production PostgreSQL must not expose a public endpoint."
  }
}

run "container_app_identity_and_replica_contract" {
  command = plan

  module {
    source = "../modules/container-app"
  }

  variables {
    name                         = "ca-test"
    container_name               = "transaction-service"
    container_app_environment_id = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/test"
    resource_group_name          = "rg-test"
    identity_id                  = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/transaction"
    registry                     = { server = "example.azurecr.io" }
    image                        = "example.azurecr.io/transaction-service:immutable-sha"
    scale                        = { min_replicas = 1, max_replicas = 2 }
    ingress                      = { external_enabled = false, target_port = 8080 }
  }

  assert {
    condition     = azurerm_container_app.this.identity[0].type == "UserAssigned"
    error_message = "Every workload must use a dedicated user-assigned identity."
  }
  assert {
    condition     = azurerm_container_app.this.template[0].min_replicas == 1
    error_message = "Showcase services must keep one warm replica."
  }
  assert {
    condition     = azurerm_container_app.this.ingress[0].external_enabled == false
    error_message = "Business services must remain internal."
  }
}
