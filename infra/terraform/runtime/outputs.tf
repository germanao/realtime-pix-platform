output "api_base_url" {
  value = "https://${azurerm_container_app.api_gateway.ingress[0].fqdn}"
}

output "presence_hub_url" {
  value = "https://${azurerm_container_app.identity_presence.ingress[0].fqdn}/presence/hub"
}

output "events_hub_url" {
  value = "https://${azurerm_container_app.realtime_events.ingress[0].fqdn}/events/hub"
}

output "apim_api_url" {
  value = "${data.terraform_remote_state.foundation.outputs.apim_gateway_url}/api"
}

output "internal_wallet_url" {
  value = "https://${azurerm_container_app.wallet_ledger.ingress[0].fqdn}"
}

output "internal_transaction_url" {
  value = "https://${azurerm_container_app.transaction.ingress[0].fqdn}"
}
