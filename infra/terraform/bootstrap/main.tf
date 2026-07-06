data "azurerm_client_config" "current" {}

resource "random_id" "suffix" {
  byte_length = 4
}

locals {
  suffix                  = lower(random_id.suffix.hex)
  tfstate_resource_group  = "rg-${var.project_name}-tfstate"
  app_resource_group      = "rg-${var.project_name}-${var.environment_name}"
  tfstate_storage_account = replace("st${replace(var.project_name, "-", "")}tf${local.suffix}", "-", "")
  tfstate_container       = "tfstate"
  github_subject_env      = "repo:${var.github_owner}/${var.github_repository}:environment:${var.github_environment_name}"
  github_subject_branch   = "repo:${var.github_owner}/${var.github_repository}:ref:refs/heads/${var.github_branch}"
  common_tags             = merge(var.tags, { suffix = local.suffix })
}

resource "azurerm_resource_group" "tfstate" {
  name     = local.tfstate_resource_group
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_resource_group" "app" {
  name     = local.app_resource_group
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_storage_account" "tfstate" {
  name                            = substr(local.tfstate_storage_account, 0, 24)
  resource_group_name             = azurerm_resource_group.tfstate.name
  location                        = azurerm_resource_group.tfstate.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = true
  tags                            = local.common_tags

  blob_properties {
    versioning_enabled  = true
    change_feed_enabled = true
    delete_retention_policy {
      days = 7
    }
    container_delete_retention_policy {
      days = 7
    }
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = local.tfstate_container
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_user_assigned_identity" "github_actions" {
  name                = "id-${var.project_name}-github-${var.environment_name}"
  resource_group_name = azurerm_resource_group.app.name
  location            = azurerm_resource_group.app.location
  tags                = local.common_tags
}

resource "azurerm_federated_identity_credential" "github_environment" {
  name                = "github-${var.github_environment_name}"
  resource_group_name = azurerm_resource_group.app.name
  parent_id           = azurerm_user_assigned_identity.github_actions.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = local.github_subject_env
}

resource "azurerm_federated_identity_credential" "github_main_branch" {
  name                = "github-${var.github_branch}"
  resource_group_name = azurerm_resource_group.app.name
  parent_id           = azurerm_user_assigned_identity.github_actions.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = local.github_subject_branch
}

resource "azurerm_role_assignment" "github_tfstate_blob_contributor" {
  scope                = azurerm_storage_account.tfstate.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.github_actions.principal_id
}

resource "azurerm_role_assignment" "github_app_contributor" {
  scope                = azurerm_resource_group.app.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.github_actions.principal_id
}

resource "azurerm_role_assignment" "github_app_rbac_administrator" {
  scope                = azurerm_resource_group.app.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.github_actions.principal_id
}

resource "azurerm_consumption_budget_subscription" "project" {
  count           = var.monthly_budget_amount > 0 && length(var.budget_contact_emails) > 0 ? 1 : 0
  name            = "budget-${var.project_name}-${var.environment_name}"
  subscription_id = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  amount          = var.monthly_budget_amount
  time_grain      = "Monthly"

  time_period {
    start_date = "2026-07-01T00:00:00Z"
    end_date   = "2027-07-01T00:00:00Z"
  }

  notification {
    enabled        = true
    threshold      = 50
    operator       = "GreaterThan"
    contact_emails = var.budget_contact_emails
  }

  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    contact_emails = var.budget_contact_emails
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    contact_emails = var.budget_contact_emails
  }
}
