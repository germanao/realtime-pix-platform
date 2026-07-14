locals {
  public_api_operations = {
    health             = { display_name = "Health", method = "GET", url_template = "/health", status_code = 200 }
    health_live        = { display_name = "Liveness", method = "GET", url_template = "/health/live", status_code = 200 }
    health_ready       = { display_name = "Readiness", method = "GET", url_template = "/health/ready", status_code = 200 }
    sessions_anonymous = { display_name = "Create anonymous session", method = "POST", url_template = "/sessions/anonymous", status_code = 200 }
    presence_users     = { display_name = "List active users", method = "GET", url_template = "/presence/users", status_code = 200 }
    presence_heartbeat = { display_name = "Presence heartbeat", method = "POST", url_template = "/presence/heartbeat", status_code = 200 }
    presence_leave     = { display_name = "Leave presence", method = "POST", url_template = "/presence/leave", status_code = 200 }
    wallet_accounts    = { display_name = "List wallet accounts", method = "GET", url_template = "/wallet/accounts", status_code = 200 }
    wallet_bootstrap = {
      display_name        = "Bootstrap user wallet", method = "POST", url_template = "/wallet/users/{userId}/bootstrap", status_code = 200
      template_parameters = ["userId"]
    }
    wallet_deposit = {
      display_name        = "Deposit funds", method = "POST", url_template = "/wallet/accounts/{accountId}/deposit", status_code = 200
      template_parameters = ["accountId"]
    }
    wallet_transactions = {
      display_name        = "Account transactions", method = "GET", url_template = "/wallet/accounts/{accountId}/transactions", status_code = 200
      template_parameters = ["accountId"]
    }
    pix_transfers = { display_name = "Request PIX transfer", method = "POST", url_template = "/pix/transfers", status_code = 202 }
    pix_transfer_by_id = {
      display_name        = "Get PIX transfer", method = "GET", url_template = "/pix/transfers/{transferId}", status_code = 200
      template_parameters = ["transferId"]
    }
    pix_transfer_transitions = {
      display_name        = "Get Saga transition history", method = "GET", url_template = "/pix/transfers/{transferId}/transitions", status_code = 200
      template_parameters = ["transferId"]
    }
    realtime_token  = { display_name = "Realtime token", method = "GET", url_template = "/realtime/token", status_code = 200 }
    events_timeline = { display_name = "Public event timeline", method = "GET", url_template = "/events/timeline", status_code = 200 }
    transfer_flow = {
      display_name        = "Transfer architecture flow", method = "GET", url_template = "/events/transfers/{transferId}/flow", status_code = 200
      template_parameters = ["transferId"]
    }
  }
  realtime_hub_apis = {
    presence = {
      display_name = "Presence Hub"
      path         = "presence/hub"
      service_url  = "https://${module.stateful_app["identity_presence"].fqdn}/presence/hub"
    }
    events = {
      display_name = "Events Hub"
      path         = "events/hub"
      service_url  = "https://${module.stateful_app["realtime_events"].fqdn}/events/hub"
    }
  }
  cors_origins_xml = join("", [for origin in var.allowed_browser_origins : "<origin>${origin}</origin>"])
}

resource "azurerm_api_management_api" "gateway" {
  name                  = "realtime-pix-gateway"
  resource_group_name   = azurerm_resource_group.this.name
  api_management_name   = module.apim.name
  revision              = "1"
  display_name          = "Realtime PIX Gateway"
  path                  = "api"
  protocols             = ["https"]
  subscription_required = false
  service_url           = "https://${module.api_gateway.fqdn}"
}

resource "azurerm_api_management_api_operation" "public_routes" {
  for_each            = local.public_api_operations
  operation_id        = each.key
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name
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

  response { status_code = each.value.status_code }
}

resource "azurerm_api_management_api_operation" "public_preflight" {
  for_each            = local.public_api_operations
  operation_id        = "preflight-${each.key}"
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name
  display_name        = "Browser preflight: ${each.value.display_name}"
  method              = "OPTIONS"
  url_template        = each.value.url_template

  dynamic "template_parameter" {
    for_each = try(each.value.template_parameters, [])
    content {
      name     = template_parameter.value
      type     = "string"
      required = true
    }
  }

  response { status_code = 204 }
}

resource "azurerm_api_management_api_policy" "gateway_cors" {
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name

  xml_content = <<-XML
    <policies>
      <inbound>
        <cors terminate-unmatched-request="true">
          <allowed-origins>${local.cors_origins_xml}</allowed-origins>
          <allowed-methods><method>GET</method><method>POST</method><method>OPTIONS</method></allowed-methods>
          <allowed-headers><header>*</header></allowed-headers>
        </cors>
        <base />
      </inbound>
      <backend><base /></backend>
      <outbound><base /></outbound>
      <on-error><base /></on-error>
    </policies>
  XML

  depends_on = [azurerm_api_management_api_operation.public_routes, azurerm_api_management_api_operation.public_preflight]
}

resource "azurerm_api_management_api" "realtime_hub" {
  for_each              = local.realtime_hub_apis
  name                  = "realtime-pix-${each.key}-hub"
  resource_group_name   = azurerm_resource_group.this.name
  api_management_name   = module.apim.name
  revision              = "1"
  display_name          = each.value.display_name
  path                  = each.value.path
  protocols             = ["https"]
  subscription_required = false
  service_url           = each.value.service_url
}

resource "azurerm_api_management_api_operation" "realtime_negotiate" {
  for_each            = local.realtime_hub_apis
  operation_id        = "${each.key}-negotiate"
  api_name            = azurerm_api_management_api.realtime_hub[each.key].name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name
  display_name        = "Negotiate ${each.value.display_name}"
  method              = "POST"
  url_template        = "/negotiate"

  response { status_code = 200 }
}

resource "azurerm_api_management_api_operation" "realtime_preflight" {
  for_each            = local.realtime_hub_apis
  operation_id        = "${each.key}-preflight"
  api_name            = azurerm_api_management_api.realtime_hub[each.key].name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name
  display_name        = "Browser preflight: ${each.value.display_name}"
  method              = "OPTIONS"
  url_template        = "/negotiate"

  response { status_code = 204 }
}

resource "azurerm_api_management_api_policy" "realtime_cors" {
  for_each            = local.realtime_hub_apis
  api_name            = azurerm_api_management_api.realtime_hub[each.key].name
  api_management_name = module.apim.name
  resource_group_name = azurerm_resource_group.this.name

  xml_content = <<-XML
    <policies>
      <inbound>
        <cors allow-credentials="true" terminate-unmatched-request="true">
          <allowed-origins>${local.cors_origins_xml}</allowed-origins>
          <allowed-methods><method>POST</method><method>OPTIONS</method></allowed-methods>
          <allowed-headers><header>*</header></allowed-headers>
        </cors>
        <base />
      </inbound>
      <backend><base /></backend>
      <outbound><base /></outbound>
      <on-error><base /></on-error>
    </policies>
  XML

  depends_on = [azurerm_api_management_api_operation.realtime_negotiate, azurerm_api_management_api_operation.realtime_preflight]
}
