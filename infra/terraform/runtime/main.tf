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

data "terraform_remote_state" "foundation" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.tfstate_resource_group_name
    storage_account_name = var.tfstate_storage_account_name
    container_name       = var.tfstate_container_name
    key                  = "foundation-poc.tfstate"
    use_azuread_auth     = true
  }
}

data "azurerm_client_config" "current" {}

locals {
  resource_group_name = data.terraform_remote_state.foundation.outputs.resource_group_name
  suffix              = data.terraform_remote_state.foundation.outputs.suffix
  acr_login_server    = data.terraform_remote_state.foundation.outputs.acr_login_server
  image_prefix        = "${local.acr_login_server}/realtime-pix"
  key_vault_uri       = data.terraform_remote_state.foundation.outputs.key_vault_uri
  common_tags         = merge(var.tags, { suffix = local.suffix })

  common_env = [
    { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
    { name = "ASPNETCORE_URLS", value = "http://0.0.0.0:8080" },
    { name = "ASPNETCORE_HTTP_PORTS", value = "8080" },
    { name = "AppConfiguration__Endpoint", value = data.terraform_remote_state.foundation.outputs.app_configuration_endpoint },
    { name = "AppConfiguration__Label", value = var.app_config_label },
    { name = "EventBus__Provider", value = "ServiceBus" },
    { name = "EventBus__ServiceBus__FullyQualifiedNamespace", value = data.terraform_remote_state.foundation.outputs.servicebus_fully_qualified_namespace },
    { name = "EventBus__ServiceBus__TopicName", value = data.terraform_remote_state.foundation.outputs.servicebus_topic_name },
    { name = "AzureSignalR__Endpoint", value = data.terraform_remote_state.foundation.outputs.signalr_endpoint },
    { name = "APPLICATIONINSIGHTS_CONNECTION_STRING", value = data.terraform_remote_state.foundation.outputs.application_insights_connection_string }
  ]

  public_api_operations = {
    health = {
      display_name = "Health"
      method       = "GET"
      url_template = "/health"
      status_code  = 200
    }
    health_live = {
      display_name = "Liveness"
      method       = "GET"
      url_template = "/health/live"
      status_code  = 200
    }
    health_ready = {
      display_name = "Readiness"
      method       = "GET"
      url_template = "/health/ready"
      status_code  = 200
    }
    sessions_anonymous = {
      display_name = "Create anonymous session"
      method       = "POST"
      url_template = "/sessions/anonymous"
      status_code  = 200
    }
    presence_users = {
      display_name = "List active users"
      method       = "GET"
      url_template = "/presence/users"
      status_code  = 200
    }
    presence_heartbeat = {
      display_name = "Presence heartbeat"
      method       = "POST"
      url_template = "/presence/heartbeat"
      status_code  = 200
    }
    presence_leave = {
      display_name = "Leave presence"
      method       = "POST"
      url_template = "/presence/leave"
      status_code  = 200
    }
    wallet_accounts = {
      display_name = "List wallet accounts"
      method       = "GET"
      url_template = "/wallet/accounts"
      status_code  = 200
    }
    wallet_bootstrap = {
      display_name        = "Bootstrap user wallet"
      method              = "POST"
      url_template        = "/wallet/users/{userId}/bootstrap"
      status_code         = 200
      template_parameters = ["userId"]
    }
    wallet_deposit = {
      display_name        = "Deposit funds"
      method              = "POST"
      url_template        = "/wallet/accounts/{accountId}/deposit"
      status_code         = 200
      template_parameters = ["accountId"]
    }
    wallet_transactions = {
      display_name        = "Account transactions"
      method              = "GET"
      url_template        = "/wallet/accounts/{accountId}/transactions"
      status_code         = 200
      template_parameters = ["accountId"]
    }
    pix_transfers = {
      display_name = "Request PIX transfer"
      method       = "POST"
      url_template = "/pix/transfers"
      status_code  = 202
    }
    pix_transfer_by_id = {
      display_name        = "Get PIX transfer"
      method              = "GET"
      url_template        = "/pix/transfers/{transferId}"
      status_code         = 200
      template_parameters = ["transferId"]
    }
    realtime_token = {
      display_name = "Realtime token"
      method       = "GET"
      url_template = "/realtime/token"
      status_code  = 200
    }
    events_timeline = {
      display_name = "Public event timeline"
      method       = "GET"
      url_template = "/events/timeline"
      status_code  = 200
    }
    transfer_flow = {
      display_name        = "Transfer architecture flow"
      method              = "GET"
      url_template        = "/events/transfers/{transferId}/flow"
      status_code         = 200
      template_parameters = ["transferId"]
    }
  }
}

resource "azurerm_user_assigned_identity" "container_apps" {
  name                = "id-${var.project_name}-apps-${var.environment_name}-${local.suffix}"
  resource_group_name = local.resource_group_name
  location            = data.terraform_remote_state.foundation.outputs.location
  tags                = local.common_tags
}

resource "azurerm_role_assignment" "apps_acr_pull" {
  scope                = data.terraform_remote_state.foundation.outputs.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.container_apps.principal_id
}

resource "azurerm_role_assignment" "apps_keyvault_secrets_user" {
  scope                = data.terraform_remote_state.foundation.outputs.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.container_apps.principal_id
}

resource "azurerm_container_app" "identity_presence" {
  name                         = "ca-presence-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "identity-db"
    key_vault_secret_id = "${local.key_vault_uri}secrets/identity-db"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "signalr-connection-string"
    key_vault_secret_id = "${local.key_vault_uri}secrets/signalr-connection-string"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  ingress {
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    cors {
      allowed_origins           = var.allowed_cors_origins
      allowed_methods           = ["GET", "POST", "OPTIONS"]
      allowed_headers           = ["*"]
      allow_credentials_enabled = true
    }

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "identity-presence-service"
      image  = "${local.image_prefix}/identity-presence-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "identity-db"
      }

      env {
        name        = "AzureSignalR__ConnectionString"
        secret_name = "signalr-connection-string"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.apps_acr_pull,
    azurerm_role_assignment.apps_keyvault_secrets_user
  ]
}

resource "azurerm_container_app" "wallet_ledger" {
  name                         = "ca-wallet-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "wallet-db"
    key_vault_secret_id = "${local.key_vault_uri}secrets/wallet-db"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  ingress {
    external_enabled           = false
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "wallet-ledger-service"
      image  = "${local.image_prefix}/wallet-ledger-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "wallet-db"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.apps_acr_pull,
    azurerm_role_assignment.apps_keyvault_secrets_user
  ]
}

resource "azurerm_container_app" "transaction" {
  name                         = "ca-tx-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "transaction-db"
    key_vault_secret_id = "${local.key_vault_uri}secrets/transaction-db"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  ingress {
    external_enabled           = false
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "transaction-service"
      image  = "${local.image_prefix}/transaction-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "transaction-db"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.apps_acr_pull,
    azurerm_role_assignment.apps_keyvault_secrets_user
  ]
}

resource "azurerm_container_app" "realtime_events" {
  name                         = "ca-events-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "realtime-db"
    key_vault_secret_id = "${local.key_vault_uri}secrets/realtime-db"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  secret {
    name                = "signalr-connection-string"
    key_vault_secret_id = "${local.key_vault_uri}secrets/signalr-connection-string"
    identity            = azurerm_user_assigned_identity.container_apps.id
  }

  ingress {
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    cors {
      allowed_origins           = var.allowed_cors_origins
      allowed_methods           = ["GET", "POST", "OPTIONS"]
      allowed_headers           = ["*"]
      allow_credentials_enabled = true
    }

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "realtime-events-service"
      image  = "${local.image_prefix}/realtime-events-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name        = "ConnectionStrings__Default"
        secret_name = "realtime-db"
      }

      env {
        name        = "AzureSignalR__ConnectionString"
        secret_name = "signalr-connection-string"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.apps_acr_pull,
    azurerm_role_assignment.apps_keyvault_secrets_user
  ]
}

resource "azurerm_container_app" "api_gateway" {
  name                         = "ca-api-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  ingress {
    external_enabled           = true
    target_port                = 8080
    transport                  = "auto"
    allow_insecure_connections = false

    cors {
      allowed_origins = var.allowed_cors_origins
      allowed_methods = ["GET", "POST", "OPTIONS"]
      allowed_headers = ["*"]
    }

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 0
    max_replicas = 2

    container {
      name   = "api-gateway"
      image  = "${local.image_prefix}/api-gateway:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name  = "Services__IdentityPresence"
        value = "https://${azurerm_container_app.identity_presence.ingress[0].fqdn}"
      }
      env {
        name  = "Services__WalletLedger"
        value = "https://${azurerm_container_app.wallet_ledger.ingress[0].fqdn}"
      }
      env {
        name  = "Services__Transaction"
        value = "https://${azurerm_container_app.transaction.ingress[0].fqdn}"
      }
      env {
        name  = "Services__RealtimeEvents"
        value = "https://${azurerm_container_app.realtime_events.ingress[0].fqdn}"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }

      readiness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/ready"
      }
    }
  }

  depends_on = [azurerm_role_assignment.apps_acr_pull]
}

resource "azurerm_container_app" "bot" {
  name                         = "ca-bot-${var.environment_name}-${local.suffix}"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  revision_mode                = "Single"
  tags                         = local.common_tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.container_apps.id]
  }

  registry {
    server   = local.acr_login_server
    identity = azurerm_user_assigned_identity.container_apps.id
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "bot-service"
      image  = "${local.image_prefix}/bot-service:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = local.common_env
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name  = "WalletServiceUrl"
        value = "https://${azurerm_container_app.wallet_ledger.ingress[0].fqdn}"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"
      }
    }
  }

  depends_on = [azurerm_role_assignment.apps_acr_pull]
}

locals {
  app_principal_ids = {
    api_gateway       = azurerm_container_app.api_gateway.identity[0].principal_id
    identity_presence = azurerm_container_app.identity_presence.identity[0].principal_id
    wallet_ledger     = azurerm_container_app.wallet_ledger.identity[0].principal_id
    transaction       = azurerm_container_app.transaction.identity[0].principal_id
    realtime_events   = azurerm_container_app.realtime_events.identity[0].principal_id
    bot               = azurerm_container_app.bot.identity[0].principal_id
  }

  container_app_ids = {
    api_gateway       = azurerm_container_app.api_gateway.id
    identity_presence = azurerm_container_app.identity_presence.id
    wallet_ledger     = azurerm_container_app.wallet_ledger.id
    transaction       = azurerm_container_app.transaction.id
    realtime_events   = azurerm_container_app.realtime_events.id
    bot               = azurerm_container_app.bot.id
  }
}

resource "azurerm_monitor_metric_alert" "container_app_restarts" {
  for_each            = local.container_app_ids
  name                = "alert-${var.project_name}-${var.environment_name}-${replace(each.key, "_", "-")}-restarts-${local.suffix}"
  resource_group_name = local.resource_group_name
  scopes              = [each.value]
  description         = "Container App ${each.key} restarted repeatedly in the POC window."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = local.common_tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "RestartCount"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = 3
  }

  action {
    action_group_id = data.terraform_remote_state.foundation.outputs.monitor_action_group_id
  }
}

resource "azurerm_role_assignment" "servicebus_data_sender" {
  for_each             = local.app_principal_ids
  scope                = data.terraform_remote_state.foundation.outputs.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "servicebus_data_receiver" {
  for_each = {
    wallet_ledger   = local.app_principal_ids.wallet_ledger
    transaction     = local.app_principal_ids.transaction
    realtime_events = local.app_principal_ids.realtime_events
  }

  scope                = data.terraform_remote_state.foundation.outputs.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "appconfig_data_reader" {
  for_each             = local.app_principal_ids
  scope                = data.terraform_remote_state.foundation.outputs.app_configuration_id
  role_definition_name = "App Configuration Data Reader"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "signalr_app_server" {
  for_each = {
    identity_presence = local.app_principal_ids.identity_presence
    realtime_events   = local.app_principal_ids.realtime_events
  }

  scope                = data.terraform_remote_state.foundation.outputs.signalr_id
  role_definition_name = "SignalR App Server"
  principal_id         = each.value
}

resource "azurerm_api_management_api" "gateway" {
  name                = "realtime-pix-gateway"
  resource_group_name = local.resource_group_name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  revision            = "1"
  display_name        = "Realtime PIX Gateway"
  path                = "api"
  protocols           = ["https"]
  service_url         = "https://${azurerm_container_app.api_gateway.ingress[0].fqdn}"
}

resource "azurerm_api_management_api_operation" "public_routes" {
  for_each            = local.public_api_operations
  operation_id        = each.key
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  resource_group_name = local.resource_group_name
  display_name        = each.value.display_name
  method              = each.value.method
  url_template        = each.value.url_template

  dynamic "template_parameter" {
    for_each = try(each.value.template_parameters, [])
    content {
      name     = template_parameter.value
      type     = "string"
      required = true
    }
  }

  response {
    status_code = each.value.status_code
  }
}

resource "azurerm_api_management_api_policy" "cors" {
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  resource_group_name = local.resource_group_name

  xml_content = <<XML
<policies>
  <inbound>
    <cors>
      <allowed-origins>
${join("", [for origin in var.allowed_cors_origins : "        <origin>${origin}</origin>\n"])}
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
        <method>POST</method>
        <method>OPTIONS</method>
      </allowed-methods>
      <allowed-headers>
        <header>*</header>
      </allowed-headers>
    </cors>
    <base />
  </inbound>
  <backend>
    <base />
  </backend>
  <outbound>
    <base />
  </outbound>
  <on-error>
    <base />
  </on-error>
</policies>
XML
}
