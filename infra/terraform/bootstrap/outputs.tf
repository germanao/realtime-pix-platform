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
  value = module.state_backend.storage_account_name
}

output "tfstate_container_name" {
  value = module.state_backend.container_name
}

output "github_actions_client_id" {
  value = module.github_apply.client_id
}

output "github_apply_client_id" {
  value = module.github_apply.client_id
}

output "github_plan_client_id" {
  value = module.github_plan.client_id
}

output "github_image_push_client_id" {
  value = module.github_image_push.client_id
}

output "github_image_push_principal_id" {
  value = module.github_image_push.principal_id
}

output "github_actions_principal_id" {
  value = module.github_apply.principal_id
}

output "github_actions_identity_name" {
  value = module.github_apply.name
}

output "tenant_id" {
  value = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  value = data.azurerm_client_config.current.subscription_id
}
