output "api_base_url" {
  value = "https://${azurerm_container_app.api_gateway.latest_revision_fqdn}"
}

output "presence_hub_url" {
  value = "https://${azurerm_container_app.identity_presence.latest_revision_fqdn}/presence/hub"
}

output "events_hub_url" {
  value = "https://${azurerm_container_app.realtime_events.latest_revision_fqdn}/events/hub"
}

output "apim_api_url" {
  value = "${data.terraform_remote_state.foundation.outputs.apim_gateway_url}/api"
}

output "internal_wallet_url" {
  value = "https://${azurerm_container_app.wallet_ledger.latest_revision_fqdn}"
}

output "internal_transaction_url" {
  value = "https://${azurerm_container_app.transaction.latest_revision_fqdn}"
}
