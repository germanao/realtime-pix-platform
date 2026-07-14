data "terraform_remote_state" "foundation" {
  backend = "azurerm"

  config = {
    resource_group_name  = var.tfstate_resource_group_name
    storage_account_name = var.tfstate_storage_account_name
    container_name       = var.tfstate_container_name
    key                  = "${var.environment_name}/foundation.tfstate"
    use_azuread_auth     = true
  }
}

locals {
  resource_group_name = data.terraform_remote_state.foundation.outputs.resource_group_name
  location            = data.terraform_remote_state.foundation.outputs.location
  suffix              = data.terraform_remote_state.foundation.outputs.suffix
  acr_login_server    = data.terraform_remote_state.foundation.outputs.acr_login_server
  image_prefix        = "${local.acr_login_server}/realtime-pix"
  common_tags = merge(var.tags, {
    suffix      = local.suffix
    environment = var.environment_name
  })

  cors_environment = merge(
    {
      Cors__AllowVercelPreviews = { value = tostring(var.allow_vercel_previews) }
      Cors__VercelProjectName   = { value = var.vercel_preview_project_name }
      Cors__VercelScopeSlug     = { value = var.vercel_preview_scope_slug }
    },
    {
      for index, origin in var.allowed_cors_origins :
      "Cors__AllowedOrigins__${index}" => { value = origin }
    }
  )
  common_environment = merge({
    ASPNETCORE_ENVIRONMENT = { value = "Production" }
    ASPNETCORE_URLS        = { value = "http://0.0.0.0:8080" }
    ASPNETCORE_HTTP_PORTS  = { value = "8080" }
    AppConfiguration__Endpoint = {
      value = data.terraform_remote_state.foundation.outputs.app_configuration_endpoint
    }
    AppConfiguration__Label = { value = var.app_config_label }
    EventBus__Provider      = { value = "ServiceBus" }
    EventBus__ServiceBus__FullyQualifiedNamespace = {
      value = data.terraform_remote_state.foundation.outputs.servicebus_fully_qualified_namespace
    }
    EventBus__ServiceBus__TopicName = {
      value = data.terraform_remote_state.foundation.outputs.servicebus_topic_name
    }
    AzureSignalR__Endpoint = {
      value = data.terraform_remote_state.foundation.outputs.signalr_endpoint
    }
    APPLICATIONINSIGHTS_CONNECTION_STRING = {
      value = data.terraform_remote_state.foundation.outputs.application_insights_connection_string
    }
  }, local.cors_environment)

  common_roles = {
    acr_pull = {
      scope                = data.terraform_remote_state.foundation.outputs.acr_id
      role_definition_name = "AcrPull"
    }
    app_configuration_reader = {
      scope                = data.terraform_remote_state.foundation.outputs.app_configuration_id
      role_definition_name = "App Configuration Data Reader"
    }
  }

  topic_sender = {
    servicebus_topic_sender = {
      scope                = data.terraform_remote_state.foundation.outputs.servicebus_topic_id
      role_definition_name = "Azure Service Bus Data Sender"
    }
  }

  workload_roles = {
    api_gateway = local.common_roles
    identity_presence = merge(local.common_roles, local.topic_sender, {
      signalr_server = {
        scope                = data.terraform_remote_state.foundation.outputs.signalr_id
        role_definition_name = "SignalR App Server"
      }
    })
    bank_a = merge(local.common_roles, local.topic_sender, {
      bank_commands_receiver = {
        scope                = data.terraform_remote_state.foundation.outputs.bank_command_queue_ids["bank-a-commands"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
    })
    bank_b = merge(local.common_roles, local.topic_sender, {
      bank_commands_receiver = {
        scope                = data.terraform_remote_state.foundation.outputs.bank_command_queue_ids["bank-b-commands"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
    })
    transaction = merge(local.common_roles, local.topic_sender, {
      saga_outcomes_receiver = {
        scope                = data.terraform_remote_state.foundation.outputs.servicebus_subscription_ids["transaction"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
      bank_a_commands_sender = {
        scope                = data.terraform_remote_state.foundation.outputs.bank_command_queue_ids["bank-a-commands"]
        role_definition_name = "Azure Service Bus Data Sender"
      }
      bank_b_commands_sender = {
        scope                = data.terraform_remote_state.foundation.outputs.bank_command_queue_ids["bank-b-commands"]
        role_definition_name = "Azure Service Bus Data Sender"
      }
    })
    realtime_events = merge(local.common_roles, local.topic_sender, {
      projection_events_receiver = {
        scope                = data.terraform_remote_state.foundation.outputs.servicebus_subscription_ids["realtime-events-v2"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
      signalr_server = {
        scope                = data.terraform_remote_state.foundation.outputs.signalr_id
        role_definition_name = "SignalR App Server"
      }
    })
    bot           = merge(local.common_roles, local.topic_sender)
    legacy_wallet = local.common_roles
  }
}

module "workload_identity" {
  for_each = local.workload_roles
  source   = "../modules/workload-identity"

  name                = "id-${var.project_name}-${replace(each.key, "_", "-")}-${var.environment_name}-${local.suffix}"
  resource_group_name = local.resource_group_name
  location            = local.location
  role_assignments    = each.value
  tags                = local.common_tags
}

locals {
  internal_apps = {
    identity_presence = {
      name           = "ca-presence-${var.environment_name}-${local.suffix}"
      container_name = "identity-presence-service"
      image          = "${local.image_prefix}/identity-presence-service:${var.image_tag}"
      identity_key   = "identity_presence"
      environment = merge(local.common_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["identity_presence"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${data.terraform_remote_state.foundation.outputs.postgres_fqdn};Port=5432;Database=identity_presence_db;Username=${module.workload_identity["identity_presence"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0"
        }
        Postgres__UseManagedIdentity = { value = "true" }
      })
      secrets = {}
      ingress = {
        external_enabled       = true
        target_port            = 8080
        cors_allowed_origins   = []
        cors_allow_credentials = false
      }
      scale = { min_replicas = 1, max_replicas = 1 }
    }
    bank_a = {
      name           = "ca-bank-a-${var.environment_name}-${local.suffix}"
      container_name = "bank-a-ledger-service"
      image          = "${local.image_prefix}/bank-ledger-service:${var.image_tag}"
      identity_key   = "bank_a"
      environment = merge(local.common_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["bank_a"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${data.terraform_remote_state.foundation.outputs.postgres_fqdn};Port=5432;Database=bank_a_ledger_db;Username=${module.workload_identity["bank_a"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0"
        }
        Postgres__UseManagedIdentity    = { value = "true" }
        Bank__Id                        = { value = "bank-a" }
        Bank__Name                      = { value = "Aurora Bank" }
        Bank__WelcomeBalance            = { value = "10000" }
        EventBus__ServiceBus__QueueName = { value = "bank-a-commands" }
      })
      secrets = {}
      ingress = {
        external_enabled       = false
        target_port            = 8080
        cors_allowed_origins   = []
        cors_allow_credentials = false
      }
      scale = { min_replicas = 1, max_replicas = 1 }
    }
    bank_b = {
      name           = "ca-bank-b-${var.environment_name}-${local.suffix}"
      container_name = "bank-b-ledger-service"
      image          = "${local.image_prefix}/bank-ledger-service:${var.image_tag}"
      identity_key   = "bank_b"
      environment = merge(local.common_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["bank_b"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${data.terraform_remote_state.foundation.outputs.postgres_fqdn};Port=5432;Database=bank_b_ledger_db;Username=${module.workload_identity["bank_b"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0"
        }
        Postgres__UseManagedIdentity    = { value = "true" }
        Bank__Id                        = { value = "bank-b" }
        Bank__Name                      = { value = "Boreal Bank" }
        Bank__WelcomeBalance            = { value = "0" }
        EventBus__ServiceBus__QueueName = { value = "bank-b-commands" }
      })
      secrets = {}
      ingress = {
        external_enabled       = false
        target_port            = 8080
        cors_allowed_origins   = []
        cors_allow_credentials = false
      }
      scale = { min_replicas = 1, max_replicas = 1 }
    }
    transaction = {
      name           = "ca-tx-${var.environment_name}-${local.suffix}"
      container_name = "transaction-service"
      image          = "${local.image_prefix}/transaction-service:${var.image_tag}"
      identity_key   = "transaction"
      environment = merge(local.common_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["transaction"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${data.terraform_remote_state.foundation.outputs.postgres_fqdn};Port=5432;Database=transaction_db;Username=${module.workload_identity["transaction"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0"
        }
        Postgres__UseManagedIdentity           = { value = "true" }
        EventBus__ServiceBus__SubscriptionName = { value = "transaction" }
        Saga__AllowFailureSimulation           = { value = "true" }
        Saga__StepTimeoutSeconds               = { value = "30" }
        Saga__SimulatedCreditTimeoutSeconds    = { value = "8" }
        Saga__CompensationTimeoutSeconds       = { value = "30" }
      })
      secrets = {}
      ingress = {
        external_enabled       = false
        target_port            = 8080
        cors_allowed_origins   = []
        cors_allow_credentials = false
      }
      scale = { min_replicas = 1, max_replicas = 1 }
    }
    realtime_events = {
      name           = "ca-events-${var.environment_name}-${local.suffix}"
      container_name = "realtime-events-service"
      image          = "${local.image_prefix}/realtime-events-service:${var.image_tag}"
      identity_key   = "realtime_events"
      environment = merge(local.common_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["realtime_events"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${data.terraform_remote_state.foundation.outputs.postgres_fqdn};Port=5432;Database=realtime_projection_db;Username=${module.workload_identity["realtime_events"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0"
        }
        Postgres__UseManagedIdentity           = { value = "true" }
        EventBus__ServiceBus__SubscriptionName = { value = "realtime-events-v2" }
      })
      secrets = {}
      ingress = {
        external_enabled       = true
        target_port            = 8080
        cors_allowed_origins   = []
        cors_allow_credentials = false
      }
      scale = { min_replicas = 1, max_replicas = 1 }
    }
  }
}

module "internal_apps" {
  for_each = local.internal_apps
  source   = "../modules/container-app"

  name                         = each.value.name
  container_name               = each.value.container_name
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  identity_id                  = module.workload_identity[each.value.identity_key].id
  registry                     = { server = local.acr_login_server }
  image                        = each.value.image
  environment                  = each.value.environment
  secrets                      = each.value.secrets
  ingress                      = each.value.ingress
  scale                        = each.value.scale
  tags                         = local.common_tags

  depends_on = [module.workload_identity]
}

module "api_gateway" {
  source = "../modules/container-app"

  name                         = "ca-api-${var.environment_name}-${local.suffix}"
  container_name               = "api-gateway"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  identity_id                  = module.workload_identity["api_gateway"].id
  registry                     = { server = local.acr_login_server }
  image                        = "${local.image_prefix}/api-gateway:${var.image_tag}"
  environment = merge(local.common_environment, {
    AZURE_CLIENT_ID            = { value = module.workload_identity["api_gateway"].client_id }
    Services__IdentityPresence = { value = "https://${module.internal_apps["identity_presence"].fqdn}" }
    Services__BankA            = { value = "https://${module.internal_apps["bank_a"].fqdn}" }
    Services__BankB            = { value = "https://${module.internal_apps["bank_b"].fqdn}" }
    Services__Transaction      = { value = "https://${module.internal_apps["transaction"].fqdn}" }
    Services__RealtimeEvents   = { value = "https://${module.internal_apps["realtime_events"].fqdn}" }
  })
  ingress = {
    external_enabled       = true
    target_port            = 8080
    cors_allowed_origins   = []
    cors_allow_credentials = false
  }
  scale = { min_replicas = 1, max_replicas = 2 }
  tags  = local.common_tags

  depends_on = [module.internal_apps]
}

module "bot" {
  source = "../modules/container-app"

  name                         = "ca-bot-${var.environment_name}-${local.suffix}"
  container_name               = "bot-service"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  identity_id                  = module.workload_identity["bot"].id
  registry                     = { server = local.acr_login_server }
  image                        = "${local.image_prefix}/bot-service:${var.image_tag}"
  environment = merge(local.common_environment, {
    AZURE_CLIENT_ID  = { value = module.workload_identity["bot"].client_id }
    WalletServiceUrl = { value = "https://${module.api_gateway.fqdn}" }
  })
  ingress = null
  scale   = { min_replicas = 1, max_replicas = 1 }
  tags    = local.common_tags

  depends_on = [module.api_gateway]
}

module "legacy_wallet" {
  source = "../modules/container-app"

  name                         = "ca-wallet-${var.environment_name}-${local.suffix}"
  container_name               = "wallet-ledger-service"
  container_app_environment_id = data.terraform_remote_state.foundation.outputs.container_app_environment_id
  resource_group_name          = local.resource_group_name
  identity_id                  = module.workload_identity["legacy_wallet"].id
  registry                     = { server = local.acr_login_server }
  image                        = "${local.image_prefix}/wallet-ledger-service:${var.image_tag}"
  environment = merge(local.common_environment, {
    AZURE_CLIENT_ID                        = { value = module.workload_identity["legacy_wallet"].client_id }
    EventBus__ServiceBus__SubscriptionName = { value = "wallet-ledger" }
  })
  secrets = {}
  ingress = {
    external_enabled       = false
    target_port            = 8080
    cors_allowed_origins   = []
    cors_allow_credentials = false
  }
  scale = { min_replicas = 0, max_replicas = 1 }
  tags  = merge(local.common_tags, { lifecycle = "legacy-one-release" })
}

locals {
  active_container_app_ids = merge(
    { for key, app in module.internal_apps : key => app.id },
    {
      api_gateway = module.api_gateway.id
      bot         = module.bot.id
    }
  )
}

resource "azurerm_monitor_metric_alert" "container_app_restarts" {
  for_each            = local.active_container_app_ids
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

moved {
  from = azurerm_container_app.identity_presence
  to   = module.internal_apps["identity_presence"].azurerm_container_app.this
}

moved {
  from = azurerm_container_app.transaction
  to   = module.internal_apps["transaction"].azurerm_container_app.this
}

moved {
  from = azurerm_container_app.realtime_events
  to   = module.internal_apps["realtime_events"].azurerm_container_app.this
}

moved {
  from = azurerm_container_app.api_gateway
  to   = module.api_gateway.azurerm_container_app.this
}

moved {
  from = azurerm_container_app.bot
  to   = module.bot.azurerm_container_app.this
}

moved {
  from = azurerm_container_app.wallet_ledger
  to   = module.legacy_wallet.azurerm_container_app.this
}
