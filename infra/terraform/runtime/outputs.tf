output "api_base_url" {
  value = "https://${module.api_gateway.fqdn}"
}

output "presence_hub_url" {
  value = "https://${module.internal_apps["identity_presence"].fqdn}/presence/hub"
}

output "events_hub_url" {
  value = "https://${module.internal_apps["realtime_events"].fqdn}/events/hub"
}

output "apim_api_url" {
  value = "${data.terraform_remote_state.foundation.outputs.apim_gateway_url}/api"
}

output "internal_bank_urls" {
  value = {
    bank_a = "https://${module.internal_apps["bank_a"].fqdn}"
    bank_b = "https://${module.internal_apps["bank_b"].fqdn}"
  }
}

output "internal_transaction_url" {
  value = "https://${module.internal_apps["transaction"].fqdn}"
}

output "workload_identity_principal_ids" {
  value = { for key, identity in module.workload_identity : key => identity.principal_id }
}

output "workload_identities" {
  value = {
    for key, identity in module.workload_identity : key => {
      name         = identity.name
      client_id    = identity.client_id
      principal_id = identity.principal_id
    }
  }
}

output "active_container_app_names" {
  value = concat(
    [for app in module.internal_apps : app.name],
    [module.api_gateway.name, module.bot.name]
  )
}

output "deployed_image_tag" {
  value = var.image_tag
}
