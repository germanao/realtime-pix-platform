data "azurerm_client_config" "current" {}

data "terraform_remote_state" "bootstrap" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.tfstate_resource_group_name
    storage_account_name = var.tfstate_storage_account_name
    container_name       = var.tfstate_container_name
    key                  = "bootstrap.tfstate"
    use_azuread_auth     = true
  }
}

resource "random_id" "suffix" {
  byte_length = 4
}

resource "random_id" "app_configuration_suffix" {
  byte_length = 4
}

resource "random_password" "postgres_admin" {
  length           = 24
  special          = true
  override_special = "!#$%&*()-_=+[]{}"
}

locals {
  suffix              = lower(random_id.suffix.hex)
  resource_group_name = data.terraform_remote_state.bootstrap.outputs.application_resource_group_name
  common_tags         = merge(var.tags, { suffix = local.suffix })

  database_names = toset([
    "identity_presence_db",
    "wallet_ledger_db",
    "transaction_db",
    "realtime_projection_db"
  ])

  servicebus_subscriptions = {
    "wallet-ledger"   = "eventType = 'PixTransferRequested.v1'"
    "transaction"     = "eventType IN ('PixDebitSucceeded.v1', 'PixDebitFailed.v1', 'PixCreditSucceeded.v1')"
    "realtime-events" = "eventType IN ('AnonymousUserJoined.v1', 'UserPresenceChanged.v1', 'AccountCreated.v1', 'FundsDeposited.v1', 'PixTransferRequested.v1', 'PixDebitSucceeded.v1', 'PixDebitFailed.v1', 'PixCreditSucceeded.v1', 'PixTransferCompleted.v1', 'PixTransferFailed.v1', 'ArchitectureFlowStepRecorded.v1')"
  }
}

data "azurerm_resource_group" "app" {
  name = local.resource_group_name
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.common_tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.common_tags
}

resource "azurerm_container_registry" "main" {
  name                = "acrrealtimepix${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.common_tags
}

resource "azurerm_postgresql_flexible_server" "main" {
  name                          = "pg-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name           = data.azurerm_resource_group.app.name
  location                      = var.location
  version                       = "16"
  administrator_login           = var.postgres_admin_login
  administrator_password        = random_password.postgres_admin.result
  sku_name                      = "B_Standard_B1ms"
  storage_mb                    = 32768
  backup_retention_days         = 7
  public_network_access_enabled = true
  tags                          = local.common_tags

  authentication {
    active_directory_auth_enabled = false
    password_auth_enabled         = true
  }

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_postgresql_flexible_server_database" "service_databases" {
  for_each  = local.database_names
  name      = each.value
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_servicebus_namespace" "main" {
  name                          = "sb-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name           = data.azurerm_resource_group.app.name
  location                      = var.location
  sku                           = "Standard"
  local_auth_enabled            = false
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
  tags                          = local.common_tags
}

resource "azurerm_servicebus_topic" "platform_events" {
  name                                    = "platform-events"
  namespace_id                            = azurerm_servicebus_namespace.main.id
  partitioning_enabled                    = true
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = "PT10M"
  default_message_ttl                     = "P14D"
}

resource "azurerm_servicebus_subscription" "consumers" {
  for_each            = local.servicebus_subscriptions
  name                = each.key
  topic_id            = azurerm_servicebus_topic.platform_events.id
  max_delivery_count  = 10
  default_message_ttl = "P14D"
}

resource "azurerm_servicebus_subscription_rule" "consumer_filters" {
  for_each        = local.servicebus_subscriptions
  name            = "event-type-filter"
  subscription_id = azurerm_servicebus_subscription.consumers[each.key].id
  filter_type     = "SqlFilter"
  sql_filter      = each.value
}

resource "azurerm_signalr_service" "main" {
  name                          = "sig-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name           = data.azurerm_resource_group.app.name
  location                      = var.location
  service_mode                  = "Default"
  public_network_access_enabled = true
  local_auth_enabled            = true
  tags                          = local.common_tags

  sku {
    name     = "Free_F1"
    capacity = 1
  }

  cors {
    allowed_origins = var.allowed_cors_origins
  }
}

resource "azurerm_key_vault" "main" {
  name                          = "kv-rtpix-${local.suffix}"
  resource_group_name           = data.azurerm_resource_group.app.name
  location                      = var.location
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  soft_delete_retention_days    = 7
  purge_protection_enabled      = false
  public_network_access_enabled = true
  tags                          = local.common_tags
}

resource "azurerm_role_assignment" "current_user_keyvault_admin" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_key_vault_secret" "postgres_admin_password" {
  name         = "postgres-admin-password"
  value        = random_password.postgres_admin.result
  key_vault_id = azurerm_key_vault.main.id
  depends_on   = [azurerm_role_assignment.current_user_keyvault_admin]
}

resource "azurerm_key_vault_secret" "database_connections" {
  for_each     = toset(["identity-db", "wallet-db", "transaction-db", "realtime-db"])
  name         = each.value
  value        = "not-configured-run-scripts-cloud-postgres-bootstrap"
  key_vault_id = azurerm_key_vault.main.id
  depends_on   = [azurerm_role_assignment.current_user_keyvault_admin]

  lifecycle {
    ignore_changes = [value]
  }
}

resource "azurerm_key_vault_secret" "signalr_connection_string" {
  name         = "signalr-connection-string"
  value        = azurerm_signalr_service.main.primary_connection_string
  key_vault_id = azurerm_key_vault.main.id
  depends_on   = [azurerm_role_assignment.current_user_keyvault_admin]
}

resource "azurerm_app_configuration" "main" {
  name                  = "appcs-${var.project_name}-${var.environment_name}-${random_id.app_configuration_suffix.hex}"
  resource_group_name   = data.azurerm_resource_group.app.name
  location              = var.location
  sku                   = "free"
  local_auth_enabled    = false
  public_network_access = "Enabled"
  tags                  = local.common_tags
}

resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name        = data.azurerm_resource_group.app.name
  location                   = var.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = local.common_tags
}

resource "azurerm_api_management" "main" {
  name                = "apim-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  publisher_name      = var.publisher_name
  publisher_email     = var.publisher_email
  sku_name            = "Consumption_0"
  tags                = local.common_tags
}

resource "azurerm_notification_hub_namespace" "main" {
  name                = "nhns-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  namespace_type      = "NotificationHub"
  sku_name            = "Free"
  tags                = local.common_tags
}

resource "azurerm_notification_hub" "main" {
  name                = "nh-${var.project_name}-${var.environment_name}"
  namespace_name      = azurerm_notification_hub_namespace.main.name
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  tags                = local.common_tags
}
