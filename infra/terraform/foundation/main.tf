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

locals {
  suffix              = lower(random_id.suffix.hex)
  resource_group_name = data.terraform_remote_state.bootstrap.outputs.application_resource_group_name
  common_tags         = merge(var.tags, { suffix = local.suffix })

  database_names = toset([
    "identity_presence_db",
    "bank_a_ledger_db",
    "bank_b_ledger_db",
    # Retained without traffic for one release after the Saga cutover.
    "wallet_ledger_db",
    "transaction_db",
    "realtime_projection_db"
  ])

  bank_command_queues = toset([
    "bank-a-commands",
    "bank-b-commands"
  ])

  servicebus_subscriptions = {
    # Preserve the subscription state while ensuring the retired consumer gets no traffic.
    "wallet-ledger"   = "1 = 0"
    "transaction"     = "eventType IN ('FundsDebited.v1', 'FundsDebitRejected.v1', 'FundsCredited.v1', 'FundsCreditRejected.v1', 'FundsRefunded.v1', 'FundsRefundRejected.v1')"
    "realtime-events" = "eventType IN ('AnonymousUserJoined.v1', 'UserPresenceChanged.v1', 'AccountCreated.v1', 'FundsDeposited.v1', 'PixTransferRequested.v1', 'PixDebitSucceeded.v1', 'PixDebitFailed.v1', 'PixCreditSucceeded.v1', 'PixTransferCompleted.v1', 'PixTransferFailed.v1', 'FundsDebited.v1', 'FundsDebitRejected.v1', 'FundsCredited.v1', 'FundsCreditRejected.v1', 'FundsRefunded.v1', 'FundsRefundRejected.v1', 'SagaTransitionRecorded.v1', 'PixSagaTimedOut.v1', 'PixTransferCompleted.v2', 'PixTransferFailed.v2', 'PixTransferCompensated.v1', 'ArchitectureFlowStepRecorded.v1')"
    # UI projections are rebuildable. A new cursor avoids coupling deployment
    # to the historical backlog of the original projection subscription.
    "realtime-events-v2" = "eventType IN ('AnonymousUserJoined.v1', 'UserPresenceChanged.v1', 'AccountCreated.v1', 'FundsDeposited.v1', 'PixTransferRequested.v1', 'PixDebitSucceeded.v1', 'PixDebitFailed.v1', 'PixCreditSucceeded.v1', 'PixTransferCompleted.v1', 'PixTransferFailed.v1', 'FundsDebited.v1', 'FundsDebitRejected.v1', 'FundsCredited.v1', 'FundsCreditRejected.v1', 'FundsRefunded.v1', 'FundsRefundRejected.v1', 'SagaTransitionRecorded.v1', 'PixSagaTimedOut.v1', 'PixTransferCompleted.v2', 'PixTransferFailed.v2', 'PixTransferCompensated.v1', 'ArchitectureFlowStepRecorded.v1')"
  }
}

data "azurerm_resource_group" "app" {
  name = local.resource_group_name
}

module "observability" {
  source = "../modules/observability"

  name_prefix         = "${var.project_name}-${var.environment_name}-${local.suffix}"
  alert_name_prefix   = "${var.project_name}-${var.environment_name}"
  name_suffix         = local.suffix
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  publisher_email     = var.publisher_email
  retention_days      = 30
  tags                = local.common_tags
}

locals {
  diagnostic_targets = {
    acr            = azurerm_container_registry.main.id
    postgresql     = module.postgresql.id
    service_bus    = module.service_bus.namespace_id
    signalr        = module.signalr.id
    key_vault      = azurerm_key_vault.main.id
    app_config     = azurerm_app_configuration.main.id
    api_management = module.apim.id
  }
}

module "diagnostic_setting" {
  for_each = local.diagnostic_targets
  source   = "../modules/diagnostic-settings"

  name                       = "diag-${replace(each.key, "_", "-")}-${local.suffix}"
  target_resource_id         = each.value
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
}

resource "azurerm_container_registry" "main" {
  name                = "acrrealtimepix${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.common_tags
}

resource "azurerm_role_assignment" "github_image_push" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPush"
  principal_id         = data.terraform_remote_state.bootstrap.outputs.github_image_push_principal_id
}

resource "azurerm_role_assignment" "github_acr_push" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPush"
  principal_id         = data.terraform_remote_state.bootstrap.outputs.github_actions_principal_id
}

module "postgresql" {
  source = "../modules/postgresql"

  name                = "pg-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  tenant_id           = data.terraform_remote_state.bootstrap.outputs.tenant_id
  administrator = {
    entra_object_id = data.terraform_remote_state.bootstrap.outputs.github_actions_principal_id
    entra_name      = data.terraform_remote_state.bootstrap.outputs.github_actions_identity_name
  }
  server = {
    sku_name              = "B_Standard_B1ms"
    storage_mb            = 32768
    backup_retention_days = 7
    public_network_access = true
    allow_azure_services  = true
  }
  databases = local.database_names
  tags      = local.common_tags
}

resource "azurerm_monitor_metric_alert" "postgres_storage_pressure" {
  name                = "alert-${var.project_name}-${var.environment_name}-postgres-storage-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  scopes              = [module.postgresql.id]
  description         = "PostgreSQL storage usage is above the POC threshold."
  severity            = 2
  frequency           = "PT15M"
  window_size         = "PT30M"
  tags                = local.common_tags

  criteria {
    metric_namespace = "Microsoft.DBforPostgreSQL/flexibleServers"
    metric_name      = "storage_percent"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 80
  }

  action {
    action_group_id = module.observability.action_group_id
  }
}

module "service_bus" {
  source = "../modules/service-bus-topology"

  name                = "sb-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  location            = var.location
  namespace = {
    sku                   = "Standard"
    public_network_access = true
  }
  topic = {
    name = "platform-events"
  }
  command_queues = { for queue in local.bank_command_queues : queue => {} }
  subscriptions  = local.servicebus_subscriptions
  tags           = local.common_tags
}

resource "azurerm_monitor_metric_alert" "servicebus_dead_letters" {
  name                = "alert-${var.project_name}-${var.environment_name}-servicebus-deadletters-${local.suffix}"
  resource_group_name = data.azurerm_resource_group.app.name
  scopes              = [module.service_bus.namespace_id]
  description         = "Service Bus dead-lettered messages were detected."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = local.common_tags

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = 0
  }

  action {
    action_group_id = module.observability.action_group_id
  }
}

module "signalr" {
  source = "../modules/signalr"

  name                  = "sig-${var.project_name}-${var.environment_name}-${local.suffix}"
  resource_group_name   = data.azurerm_resource_group.app.name
  location              = var.location
  sku_name              = "Free_F1"
  capacity              = 1
  public_network_access = true
  allowed_origins       = var.allowed_cors_origins
  tags                  = local.common_tags
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
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
  tags                       = local.common_tags
}

module "apim" {
  source = "../modules/apim"

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

moved {
  from = random_uuid.workbook
  to   = module.observability.random_uuid.workbook
}

moved {
  from = azurerm_log_analytics_workspace.main
  to   = module.observability.azurerm_log_analytics_workspace.this
}

moved {
  from = azurerm_application_insights.main
  to   = module.observability.azurerm_application_insights.this
}

moved {
  from = azurerm_application_insights_workbook.showcase
  to   = module.observability.azurerm_application_insights_workbook.showcase
}

moved {
  from = azurerm_monitor_action_group.showcase
  to   = module.observability.azurerm_monitor_action_group.this
}

moved {
  from = azurerm_monitor_scheduled_query_rules_alert_v2.outbox_failures
  to   = module.observability.azurerm_monitor_scheduled_query_rules_alert_v2.outbox_failures
}

moved {
  from = azurerm_monitor_scheduled_query_rules_alert_v2.api_failed_requests
  to   = module.observability.azurerm_monitor_scheduled_query_rules_alert_v2.api_failures
}

moved {
  from = azurerm_postgresql_flexible_server.main
  to   = module.postgresql.azurerm_postgresql_flexible_server.this
}

moved {
  from = azurerm_api_management.main
  to   = module.apim.azurerm_api_management.this
}

moved {
  from = azurerm_signalr_service.main
  to   = module.signalr.azurerm_signalr_service.this
}

moved {
  from = azurerm_servicebus_namespace.main
  to   = module.service_bus.azurerm_servicebus_namespace.this
}

moved {
  from = azurerm_servicebus_topic.platform_events
  to   = module.service_bus.azurerm_servicebus_topic.events
}

moved {
  from = azurerm_servicebus_queue.bank_commands
  to   = module.service_bus.azurerm_servicebus_queue.commands
}

moved {
  from = azurerm_servicebus_subscription.consumers
  to   = module.service_bus.azurerm_servicebus_subscription.consumers
}

moved {
  from = azurerm_servicebus_subscription_rule.consumer_filters
  to   = module.service_bus.azurerm_servicebus_subscription_rule.filters
}

moved {
  from = azurerm_postgresql_flexible_server_active_directory_administrator.github_actions
  to   = module.postgresql.azurerm_postgresql_flexible_server_active_directory_administrator.this
}

moved {
  from = azurerm_postgresql_flexible_server_firewall_rule.allow_azure_services
  to   = module.postgresql.azurerm_postgresql_flexible_server_firewall_rule.azure_services[0]
}

moved {
  from = azurerm_postgresql_flexible_server_database.service_databases
  to   = module.postgresql.azurerm_postgresql_flexible_server_database.this
}
