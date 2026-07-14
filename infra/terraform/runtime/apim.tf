locals {
  apim_exact_cors_checks = [
    for origin in var.allowed_cors_origins : "origin == \"${origin}\""
  ]
  apim_preview_cors_checks = var.allow_vercel_previews ? [
    "(origin.StartsWith(\"https://${var.vercel_preview_project_name}-\") &amp;&amp; origin.EndsWith(\"-${var.vercel_preview_scope_slug}.vercel.app\"))"
  ] : []
  apim_cors_expression = join(
    "\n              || ",
    concat(local.apim_exact_cors_checks, local.apim_preview_cors_checks)
  )

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
}

resource "azurerm_api_management_api" "gateway" {
  name                  = "realtime-pix-gateway"
  resource_group_name   = local.resource_group_name
  api_management_name   = data.terraform_remote_state.foundation.outputs.apim_name
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

resource "azurerm_api_management_api_operation" "browser_preflight" {
  for_each            = local.public_api_operations
  operation_id        = "preflight-${each.key}"
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  resource_group_name = local.resource_group_name
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

  response {
    status_code = 204
  }
}

resource "azurerm_api_management_api_operation_policy" "browser_preflight" {
  for_each            = local.public_api_operations
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  resource_group_name = local.resource_group_name
  operation_id        = azurerm_api_management_api_operation.browser_preflight[each.key].operation_id

  xml_content = <<-XML
    <policies>
      <inbound>
        <base />
        <choose>
          <when condition='@{
            var origin = context.Request.Headers.GetValueOrDefault("Origin", "");
            return ${local.apim_cors_expression};
          }'>
            <return-response>
              <set-status code="204" reason="No Content" />
              <set-header name="Access-Control-Allow-Origin" exists-action="override"><value>@(context.Request.Headers.GetValueOrDefault("Origin", ""))</value></set-header>
              <set-header name="Access-Control-Allow-Methods" exists-action="override"><value>GET, POST, OPTIONS</value></set-header>
              <set-header name="Access-Control-Allow-Headers" exists-action="override"><value>content-type, accept, idempotency-key</value></set-header>
              <set-header name="Access-Control-Max-Age" exists-action="override"><value>300</value></set-header>
              <set-header name="Vary" exists-action="override"><value>Origin</value></set-header>
            </return-response>
          </when>
          <otherwise><return-response><set-status code="403" reason="Origin not allowed" /></return-response></otherwise>
        </choose>
      </inbound>
      <backend><base /></backend>
      <outbound><base /></outbound>
      <on-error><base /></on-error>
    </policies>
  XML
}

resource "azurerm_api_management_api_policy" "browser_cors" {
  api_name            = azurerm_api_management_api.gateway.name
  api_management_name = data.terraform_remote_state.foundation.outputs.apim_name
  resource_group_name = local.resource_group_name

  xml_content = <<-XML
    <policies>
      <inbound><base /></inbound>
      <backend><base /></backend>
      <outbound>
        <base />
        <choose>
          <when condition='@{
            var origin = context.Request.Headers.GetValueOrDefault("Origin", "");
            return ${local.apim_cors_expression};
          }'>
            <set-header name="Access-Control-Allow-Origin" exists-action="override"><value>@(context.Request.Headers.GetValueOrDefault("Origin", ""))</value></set-header>
            <set-header name="Vary" exists-action="override"><value>Origin</value></set-header>
          </when>
        </choose>
      </outbound>
      <on-error><base /></on-error>
    </policies>
  XML

  depends_on = [azurerm_api_management_api_operation.public_routes]
}
