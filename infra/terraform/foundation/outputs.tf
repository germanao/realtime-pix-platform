output "resource_group_name" {
  value = data.azurerm_resource_group.app.name
}

output "location" {
  value = var.location
}

output "suffix" {
  value = local.suffix
}

output "acr_name" {
  value = azurerm_container_registry.main.name
}

output "acr_id" {
  value = azurerm_container_registry.main.id
}

output "acr_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "postgres_fqdn" {
  value = module.postgresql.fqdn
}

output "postgres_server_name" {
  value = module.postgresql.name
}

output "postgres_entra_admin_name" {
  value = data.terraform_remote_state.bootstrap.outputs.github_actions_identity_name
}

output "postgres_entra_admin_object_id" {
  value = data.terraform_remote_state.bootstrap.outputs.github_actions_principal_id
}

output "servicebus_namespace_name" {
  value = module.service_bus.namespace_name
}

output "servicebus_namespace_id" {
  value = module.service_bus.namespace_id
}

output "servicebus_fully_qualified_namespace" {
  value = module.service_bus.fully_qualified_namespace
}

output "servicebus_topic_name" {
  value = module.service_bus.topic_name
}

output "bank_command_queue_names" {
  value = { for key in keys(module.service_bus.queue_ids) : key => key }
}

output "bank_command_queue_ids" {
  value = module.service_bus.queue_ids
}

output "servicebus_topic_id" {
  value = module.service_bus.topic_id
}

output "servicebus_subscription_ids" {
  value = module.service_bus.subscription_ids
}

output "signalr_id" {
  value = module.signalr.id
}

output "signalr_endpoint" {
  value = module.signalr.endpoint
}

output "key_vault_id" {
  value = azurerm_key_vault.main.id
}

output "key_vault_name" {
  value = azurerm_key_vault.main.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}

output "app_configuration_id" {
  value = azurerm_app_configuration.main.id
}

output "app_configuration_endpoint" {
  value = azurerm_app_configuration.main.endpoint
}

output "application_insights_connection_string" {
  value     = module.observability.application_insights_connection_string
  sensitive = true
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.main.id
}

output "apim_name" {
  value = module.apim.name
}

output "apim_gateway_url" {
  value = module.apim.gateway_url
}

output "monitor_action_group_id" {
  value = module.observability.action_group_id
}
