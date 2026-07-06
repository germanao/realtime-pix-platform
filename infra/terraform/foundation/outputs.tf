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
  value = azurerm_postgresql_flexible_server.main.fqdn
}

output "postgres_server_name" {
  value = azurerm_postgresql_flexible_server.main.name
}

output "postgres_admin_login" {
  value = var.postgres_admin_login
}

output "servicebus_namespace_name" {
  value = azurerm_servicebus_namespace.main.name
}

output "servicebus_namespace_id" {
  value = azurerm_servicebus_namespace.main.id
}

output "servicebus_fully_qualified_namespace" {
  value = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
}

output "servicebus_topic_name" {
  value = azurerm_servicebus_topic.platform_events.name
}

output "signalr_id" {
  value = azurerm_signalr_service.main.id
}

output "signalr_endpoint" {
  value = "https://${azurerm_signalr_service.main.hostname}"
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
  value     = azurerm_application_insights.main.connection_string
  sensitive = true
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.main.id
}

output "apim_name" {
  value = azurerm_api_management.main.name
}

output "apim_gateway_url" {
  value = azurerm_api_management.main.gateway_url
}
