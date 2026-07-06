output "suffix" {
  value = local.suffix
}

output "application_resource_group_name" {
  value = azurerm_resource_group.app.name
}

output "tfstate_resource_group_name" {
  value = azurerm_resource_group.tfstate.name
}

output "tfstate_storage_account_name" {
  value = azurerm_storage_account.tfstate.name
}

output "tfstate_container_name" {
  value = azurerm_storage_container.tfstate.name
}

output "github_actions_client_id" {
  value = azurerm_user_assigned_identity.github_actions.client_id
}

output "github_actions_principal_id" {
  value = azurerm_user_assigned_identity.github_actions.principal_id
}

output "tenant_id" {
  value = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  value = data.azurerm_client_config.current.subscription_id
}
