resource "random_id" "suffix" { byte_length = 4 }

locals {
  suffix        = lower(random_id.suffix.hex)
  name          = "${var.project_name}-${var.environment_name}-${local.suffix}"
  tags          = merge(var.tags, { suffix = local.suffix })
  postgres_zone = "${var.project_name}.${var.environment_name}.postgres.database.azure.com"
  databases = {
    identity    = { database = "identity_presence_db", subnet = "postgres-identity" }
    bank_a      = { database = "bank_a_ledger_db", subnet = "postgres-bank-a" }
    bank_b      = { database = "bank_b_ledger_db", subnet = "postgres-bank-b" }
    transaction = { database = "transaction_db", subnet = "postgres-transaction" }
    realtime    = { database = "realtime_projection_db", subnet = "postgres-realtime" }
  }
  event_filters = {
    transaction     = "eventType IN ('FundsDebited.v1', 'FundsDebitRejected.v1', 'FundsCredited.v1', 'FundsCreditRejected.v1', 'FundsRefunded.v1', 'FundsRefundRejected.v1')"
    realtime-events = "eventType IN ('AnonymousUserJoined.v1', 'UserPresenceChanged.v1', 'AccountCreated.v1', 'FundsDeposited.v1', 'FundsDebited.v1', 'FundsDebitRejected.v1', 'FundsCredited.v1', 'FundsCreditRejected.v1', 'FundsRefunded.v1', 'FundsRefundRejected.v1', 'SagaTransitionRecorded.v1', 'PixSagaTimedOut.v1', 'PixTransferCompleted.v2', 'PixTransferFailed.v2', 'PixTransferCompensated.v1')"
  }
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${var.project_name}-${var.environment_name}"
  location = var.location
  tags     = local.tags
}

module "networking" {
  source = "../modules/networking"

  name                = "vnet-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  address_space       = ["10.42.0.0/20"]
  subnets = {
    container-apps       = { address_prefixes = ["10.42.0.0/23"], delegation_name = "Microsoft.App/environments" }
    private-endpoints    = { address_prefixes = ["10.42.2.0/24"] }
    apim                 = { address_prefixes = ["10.42.3.0/26"], delegation_name = "Microsoft.Web/serverFarms" }
    postgres-identity    = { address_prefixes = ["10.42.4.0/28"], delegation_name = "Microsoft.DBforPostgreSQL/flexibleServers" }
    postgres-bank-a      = { address_prefixes = ["10.42.4.16/28"], delegation_name = "Microsoft.DBforPostgreSQL/flexibleServers" }
    postgres-bank-b      = { address_prefixes = ["10.42.4.32/28"], delegation_name = "Microsoft.DBforPostgreSQL/flexibleServers" }
    postgres-transaction = { address_prefixes = ["10.42.4.48/28"], delegation_name = "Microsoft.DBforPostgreSQL/flexibleServers" }
    postgres-realtime    = { address_prefixes = ["10.42.4.64/28"], delegation_name = "Microsoft.DBforPostgreSQL/flexibleServers" }
  }
  private_dns_zones = [
    local.postgres_zone,
    "privatelink.azurecr.io",
    "privatelink.servicebus.windows.net",
    "privatelink.service.signalr.net",
    "privatelink.vaultcore.azure.net",
    "privatelink.azconfig.io"
  ]
  tags = local.tags
}

module "observability" {
  source = "../modules/observability"

  name_prefix           = local.name
  alert_name_prefix     = "${var.project_name}-${var.environment_name}"
  name_suffix           = local.suffix
  resource_group_name   = azurerm_resource_group.this.name
  location              = var.location
  publisher_email       = var.publisher_email
  retention_days        = 90
  api_failure_threshold = 1
  tags                  = local.tags
}

resource "azurerm_container_registry" "this" {
  name                          = "acrrealtimepixprod${local.suffix}"
  resource_group_name           = azurerm_resource_group.this.name
  location                      = var.location
  sku                           = "Premium"
  admin_enabled                 = false
  public_network_access_enabled = false
  data_endpoint_enabled         = true
  zone_redundancy_enabled       = true
  tags                          = local.tags
}

module "service_bus" {
  source = "../modules/service-bus-topology"

  name                = "sb-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  namespace = {
    sku                   = "Premium"
    capacity              = 1
    premium_partitions    = 1
    public_network_access = false
  }
  topic = { name = "platform-events", partitioning_enabled = false }
  command_queues = {
    bank-a-commands = {}
    bank-b-commands = {}
  }
  subscriptions = local.event_filters
  tags          = local.tags
}

module "signalr" {
  source = "../modules/signalr"

  name                  = "sig-${local.name}"
  resource_group_name   = azurerm_resource_group.this.name
  location              = var.location
  sku_name              = "Standard_S1"
  capacity              = 1
  public_network_access = true
  allowed_origins       = var.allowed_browser_origins
  tags                  = local.tags
}

resource "azurerm_key_vault" "this" {
  name                          = "kv-rtpix-prod-${local.suffix}"
  resource_group_name           = azurerm_resource_group.this.name
  location                      = var.location
  tenant_id                     = var.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  soft_delete_retention_days    = 90
  purge_protection_enabled      = true
  public_network_access_enabled = false
  tags                          = local.tags

  network_acls {
    bypass         = "None"
    default_action = "Deny"
  }
}

resource "azurerm_app_configuration" "this" {
  name                     = "appcs-${local.name}"
  resource_group_name      = azurerm_resource_group.this.name
  location                 = var.location
  sku                      = "standard"
  local_auth_enabled       = false
  public_network_access    = "Disabled"
  purge_protection_enabled = true
  tags                     = local.tags
}

resource "azurerm_container_app_environment" "this" {
  name                           = "cae-${local.name}"
  resource_group_name            = azurerm_resource_group.this.name
  location                       = var.location
  log_analytics_workspace_id     = module.observability.log_analytics_workspace_id
  infrastructure_subnet_id       = module.networking.subnet_ids["container-apps"]
  internal_load_balancer_enabled = true
  zone_redundancy_enabled        = true
  tags                           = local.tags

  workload_profile {
    name                  = "production-d4"
    workload_profile_type = "D4"
    minimum_count         = 1
    maximum_count         = 3
  }
}

module "apim" {
  source = "../modules/apim"

  name                = "apim-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  publisher_name      = "Realtime PIX Platform"
  publisher_email     = var.publisher_email
  sku_name            = "StandardV2_1"
  outbound_subnet_id  = module.networking.subnet_ids["apim"]
  tags                = local.tags
}

module "postgresql" {
  for_each = local.databases
  source   = "../modules/postgresql"

  name                = "pg-${replace(each.key, "_", "-")}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tenant_id           = var.tenant_id
  administrator = {
    entra_object_id = var.entra_admin_object_id
    entra_name      = var.entra_admin_name
  }
  server = {
    sku_name               = "GP_Standard_D2ds_v5"
    storage_mb             = 131072
    backup_retention_days  = 35
    geo_redundant_backup   = true
    public_network_access  = false
    delegated_subnet_id    = module.networking.subnet_ids[each.value.subnet]
    private_dns_zone_id    = module.networking.private_dns_zone_ids[local.postgres_zone]
    high_availability_mode = "ZoneRedundant"
    allow_azure_services   = false
  }
  databases = [each.value.database]
  tags      = merge(local.tags, { data_owner = each.key })
}

module "private_endpoint" {
  for_each = {
    acr        = { resource_id = azurerm_container_registry.this.id, subresource = "registry", dns = "privatelink.azurecr.io" }
    servicebus = { resource_id = module.service_bus.namespace_id, subresource = "namespace", dns = "privatelink.servicebus.windows.net" }
    signalr    = { resource_id = module.signalr.id, subresource = "signalr", dns = "privatelink.service.signalr.net" }
    keyvault   = { resource_id = azurerm_key_vault.this.id, subresource = "vault", dns = "privatelink.vaultcore.azure.net" }
    appconfig  = { resource_id = azurerm_app_configuration.this.id, subresource = "configurationStores", dns = "privatelink.azconfig.io" }
  }
  source = "../modules/private-endpoint"

  name                           = "pe-${each.key}-${local.suffix}"
  resource_group_name            = azurerm_resource_group.this.name
  location                       = var.location
  subnet_id                      = module.networking.subnet_ids["private-endpoints"]
  private_connection_resource_id = each.value.resource_id
  subresource_names              = [each.value.subresource]
  private_dns_zone_ids           = [module.networking.private_dns_zone_ids[each.value.dns]]
  tags                           = local.tags
}

resource "azurerm_notification_hub_namespace" "this" {
  name                = "nhns-${local.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  namespace_type      = "NotificationHub"
  sku_name            = "Standard"
  tags                = local.tags
}

resource "azurerm_notification_hub" "this" {
  name                = "nh-${var.project_name}-${var.environment_name}"
  namespace_name      = azurerm_notification_hub_namespace.this.name
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  tags                = local.tags
}

resource "azurerm_private_dns_zone" "container_apps" {
  name                = azurerm_container_app_environment.this.default_domain
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "container_apps" {
  name                  = "link-container-apps-${local.suffix}"
  resource_group_name   = azurerm_resource_group.this.name
  private_dns_zone_name = azurerm_private_dns_zone.container_apps.name
  virtual_network_id    = module.networking.virtual_network_id
  registration_enabled  = false
  tags                  = local.tags
}

resource "azurerm_private_dns_a_record" "container_apps_wildcard" {
  name                = "*"
  zone_name           = azurerm_private_dns_zone.container_apps.name
  resource_group_name = azurerm_resource_group.this.name
  ttl                 = 60
  records             = [azurerm_container_app_environment.this.static_ip_address]
  tags                = local.tags
}

locals {
  image_prefix = "${azurerm_container_registry.this.login_server}/realtime-pix"
  cors_environment = merge(
    { Cors__AllowVercelPreviews = { value = "false" } },
    {
      for index, origin in var.allowed_browser_origins :
      "Cors__AllowedOrigins__${index}" => { value = origin }
    }
  )
  runtime_environment = merge({
    ASPNETCORE_ENVIRONMENT = { value = "Production" }
    ASPNETCORE_URLS        = { value = "http://0.0.0.0:8080" }
    ASPNETCORE_HTTP_PORTS  = { value = "8080" }
    AppConfiguration__Endpoint = {
      value = azurerm_app_configuration.this.endpoint
    }
    AppConfiguration__Label = { value = "production" }
    EventBus__Provider      = { value = "ServiceBus" }
    EventBus__ServiceBus__FullyQualifiedNamespace = {
      value = module.service_bus.fully_qualified_namespace
    }
    EventBus__ServiceBus__TopicName = {
      value = module.service_bus.topic_name
    }
    AzureSignalR__Endpoint = { value = module.signalr.endpoint }
    APPLICATIONINSIGHTS_CONNECTION_STRING = {
      value = module.observability.application_insights_connection_string
    }
  }, local.cors_environment)

  common_workload_roles = {
    acr_pull = {
      scope                = azurerm_container_registry.this.id
      role_definition_name = "AcrPull"
    }
    app_configuration_reader = {
      scope                = azurerm_app_configuration.this.id
      role_definition_name = "App Configuration Data Reader"
    }
  }
  topic_sender = {
    platform_events_sender = {
      scope                = module.service_bus.topic_id
      role_definition_name = "Azure Service Bus Data Sender"
    }
  }
  workload_roles = {
    api_gateway = local.common_workload_roles
    identity_presence = merge(local.common_workload_roles, local.topic_sender, {
      signalr_server = {
        scope                = module.signalr.id
        role_definition_name = "SignalR App Server"
      }
    })
    bank_a = merge(local.common_workload_roles, local.topic_sender, {
      bank_commands_receiver = {
        scope                = module.service_bus.queue_ids["bank-a-commands"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
    })
    bank_b = merge(local.common_workload_roles, local.topic_sender, {
      bank_commands_receiver = {
        scope                = module.service_bus.queue_ids["bank-b-commands"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
    })
    transaction = merge(local.common_workload_roles, local.topic_sender, {
      saga_outcomes_receiver = {
        scope                = module.service_bus.subscription_ids["transaction"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
      bank_a_commands_sender = {
        scope                = module.service_bus.queue_ids["bank-a-commands"]
        role_definition_name = "Azure Service Bus Data Sender"
      }
      bank_b_commands_sender = {
        scope                = module.service_bus.queue_ids["bank-b-commands"]
        role_definition_name = "Azure Service Bus Data Sender"
      }
    })
    realtime_events = merge(local.common_workload_roles, local.topic_sender, {
      projection_events_receiver = {
        scope                = module.service_bus.subscription_ids["realtime-events"]
        role_definition_name = "Azure Service Bus Data Receiver"
      }
      signalr_server = {
        scope                = module.signalr.id
        role_definition_name = "SignalR App Server"
      }
    })
    bot = merge(local.common_workload_roles, local.topic_sender)
  }
}

module "workload_identity" {
  for_each = local.workload_roles
  source   = "../modules/workload-identity"

  name                = "id-${var.project_name}-${replace(each.key, "_", "-")}-${var.environment_name}-${local.suffix}"
  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  role_assignments    = each.value
  tags                = merge(local.tags, { workload = each.key })
}

locals {
  stateful_apps = {
    identity_presence = {
      name           = "ca-presence-${local.name}"
      container_name = "identity-presence-service"
      image          = "${local.image_prefix}/identity-presence-service:${var.image_tag}"
      identity_key   = "identity_presence"
      environment = merge(local.runtime_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["identity_presence"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${module.postgresql["identity"].fqdn};Port=5432;Database=identity_presence_db;Username=${module.workload_identity["identity_presence"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=20;Minimum Pool Size=2"
        }
        Postgres__UseManagedIdentity = { value = "true" }
      })
      ingress = { external_enabled = true, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
    }
    bank_a = {
      name           = "ca-bank-a-${local.name}"
      container_name = "bank-a-ledger-service"
      image          = "${local.image_prefix}/bank-ledger-service:${var.image_tag}"
      identity_key   = "bank_a"
      environment = merge(local.runtime_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["bank_a"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${module.postgresql["bank_a"].fqdn};Port=5432;Database=bank_a_ledger_db;Username=${module.workload_identity["bank_a"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=20;Minimum Pool Size=2"
        }
        Postgres__UseManagedIdentity    = { value = "true" }
        Bank__Id                        = { value = "bank-a" }
        Bank__Name                      = { value = "Aurora Bank" }
        Bank__WelcomeBalance            = { value = "10000" }
        EventBus__ServiceBus__QueueName = { value = "bank-a-commands" }
      })
      ingress = { external_enabled = false, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
    }
    bank_b = {
      name           = "ca-bank-b-${local.name}"
      container_name = "bank-b-ledger-service"
      image          = "${local.image_prefix}/bank-ledger-service:${var.image_tag}"
      identity_key   = "bank_b"
      environment = merge(local.runtime_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["bank_b"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${module.postgresql["bank_b"].fqdn};Port=5432;Database=bank_b_ledger_db;Username=${module.workload_identity["bank_b"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=20;Minimum Pool Size=2"
        }
        Postgres__UseManagedIdentity    = { value = "true" }
        Bank__Id                        = { value = "bank-b" }
        Bank__Name                      = { value = "Boreal Bank" }
        Bank__WelcomeBalance            = { value = "0" }
        EventBus__ServiceBus__QueueName = { value = "bank-b-commands" }
      })
      ingress = { external_enabled = false, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
    }
    transaction = {
      name           = "ca-transaction-${local.name}"
      container_name = "transaction-service"
      image          = "${local.image_prefix}/transaction-service:${var.image_tag}"
      identity_key   = "transaction"
      environment = merge(local.runtime_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["transaction"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${module.postgresql["transaction"].fqdn};Port=5432;Database=transaction_db;Username=${module.workload_identity["transaction"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=20;Minimum Pool Size=2"
        }
        Postgres__UseManagedIdentity           = { value = "true" }
        EventBus__ServiceBus__SubscriptionName = { value = "transaction" }
        Saga__AllowFailureSimulation           = { value = "false" }
        Saga__StepTimeoutSeconds               = { value = "120" }
        Saga__CompensationTimeoutSeconds       = { value = "120" }
      })
      ingress = { external_enabled = false, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
    }
    realtime_events = {
      name           = "ca-events-${local.name}"
      container_name = "realtime-events-service"
      image          = "${local.image_prefix}/realtime-events-service:${var.image_tag}"
      identity_key   = "realtime_events"
      environment = merge(local.runtime_environment, {
        AZURE_CLIENT_ID = { value = module.workload_identity["realtime_events"].client_id }
        ConnectionStrings__Default = {
          value = "Host=${module.postgresql["realtime"].fqdn};Port=5432;Database=realtime_projection_db;Username=${module.workload_identity["realtime_events"].name};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=20;Minimum Pool Size=2"
        }
        Postgres__UseManagedIdentity           = { value = "true" }
        EventBus__ServiceBus__SubscriptionName = { value = "realtime-events" }
      })
      ingress = { external_enabled = true, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
    }
  }
}

module "stateful_app" {
  for_each = local.stateful_apps
  source   = "../modules/container-app"

  name                         = each.value.name
  container_name               = each.value.container_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  workload_profile_name        = "production-d4"
  identity_id                  = module.workload_identity[each.value.identity_key].id
  registry                     = { server = azurerm_container_registry.this.login_server }
  image                        = each.value.image
  environment                  = each.value.environment
  ingress                      = each.value.ingress
  scale                        = { min_replicas = 2, max_replicas = 6 }
  resources                    = { cpu = 0.5, memory = "1Gi" }
  tags                         = merge(local.tags, { workload = each.key })

  depends_on = [module.private_endpoint, azurerm_private_dns_a_record.container_apps_wildcard]
}

module "api_gateway" {
  source = "../modules/container-app"

  name                         = "ca-api-${local.name}"
  container_name               = "api-gateway"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  workload_profile_name        = "production-d4"
  identity_id                  = module.workload_identity["api_gateway"].id
  registry                     = { server = azurerm_container_registry.this.login_server }
  image                        = "${local.image_prefix}/api-gateway:${var.image_tag}"
  environment = merge(local.runtime_environment, {
    AZURE_CLIENT_ID            = { value = module.workload_identity["api_gateway"].client_id }
    Services__IdentityPresence = { value = "https://${module.stateful_app["identity_presence"].fqdn}" }
    Services__BankA            = { value = "https://${module.stateful_app["bank_a"].fqdn}" }
    Services__BankB            = { value = "https://${module.stateful_app["bank_b"].fqdn}" }
    Services__Transaction      = { value = "https://${module.stateful_app["transaction"].fqdn}" }
    Services__RealtimeEvents   = { value = "https://${module.stateful_app["realtime_events"].fqdn}" }
  })
  ingress   = { external_enabled = true, target_port = 8080, cors_allowed_origins = [], cors_allow_credentials = false }
  scale     = { min_replicas = 2, max_replicas = 10 }
  resources = { cpu = 0.5, memory = "1Gi" }
  tags      = merge(local.tags, { workload = "api_gateway" })
}

module "bot" {
  source = "../modules/container-app"

  name                         = "ca-bot-${local.name}"
  container_name               = "bot-service"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  workload_profile_name        = "production-d4"
  identity_id                  = module.workload_identity["bot"].id
  registry                     = { server = azurerm_container_registry.this.login_server }
  image                        = "${local.image_prefix}/bot-service:${var.image_tag}"
  environment = merge(local.runtime_environment, {
    AZURE_CLIENT_ID  = { value = module.workload_identity["bot"].client_id }
    WalletServiceUrl = { value = "https://${module.api_gateway.fqdn}" }
  })
  ingress   = null
  scale     = { min_replicas = 1, max_replicas = 1 }
  resources = { cpu = 0.25, memory = "0.5Gi" }
  tags      = merge(local.tags, { workload = "bot" })
}

locals {
  diagnostic_targets = merge(
    {
      acr            = azurerm_container_registry.this.id
      service_bus    = module.service_bus.namespace_id
      signalr        = module.signalr.id
      key_vault      = azurerm_key_vault.this.id
      app_config     = azurerm_app_configuration.this.id
      api_management = module.apim.id
    },
    { for key, server in module.postgresql : "postgresql_${key}" => server.id }
  )
}

module "diagnostic_setting" {
  for_each = local.diagnostic_targets
  source   = "../modules/diagnostic-settings"

  name                       = "diag-${replace(each.key, "_", "-")}-${local.suffix}"
  target_resource_id         = each.value
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
}
