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
  github_subject_pr       = "repo:${var.github_owner}/${var.github_repository}:pull_request"
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

module "state_backend" {
  source = "../modules/state-backend"

  storage_account_name = substr(local.tfstate_storage_account, 0, 24)
  resource_group_name  = azurerm_resource_group.tfstate.name
  location             = azurerm_resource_group.tfstate.location
  container_name       = local.tfstate_container
  retention_days       = 7
  tags                 = local.common_tags
}

module "github_apply" {
  source = "../modules/github-oidc"

  name                = "id-${var.project_name}-github-${var.environment_name}"
  resource_group_name = azurerm_resource_group.app.name
  location            = azurerm_resource_group.app.location
  subjects = {
    environment = {
      name    = "github-${var.github_environment_name}"
      subject = local.github_subject_env
    }
    branch = {
      name    = "github-${var.github_branch}"
      subject = local.github_subject_branch
    }
  }
  role_assignments = {
    tfstate = {
      scope                = module.state_backend.storage_account_id
      role_definition_name = "Storage Blob Data Contributor"
    }
    app_contributor = {
      scope                = azurerm_resource_group.app.id
      role_definition_name = "Contributor"
    }
    app_rbac = {
      scope                = azurerm_resource_group.app.id
      role_definition_name = "Role Based Access Control Administrator"
    }
  }
  tags = local.common_tags
}

module "github_plan" {
  source = "../modules/github-oidc"

  name                = "id-${var.project_name}-github-plan-${var.environment_name}"
  resource_group_name = azurerm_resource_group.app.name
  location            = azurerm_resource_group.app.location
  subjects = {
    pull_request = {
      name    = "github-pull-request-plan"
      subject = local.github_subject_pr
    }
    branch = {
      name    = "github-${var.github_branch}-plan"
      subject = local.github_subject_branch
    }
  }
  role_assignments = {
    tfstate = {
      scope                = module.state_backend.storage_account_id
      role_definition_name = "Storage Blob Data Reader"
    }
    app_reader = {
      scope                = azurerm_resource_group.app.id
      role_definition_name = "Reader"
    }
  }
  tags = merge(local.common_tags, { responsibility = "terraform-plan" })
}

module "github_image_push" {
  source = "../modules/github-oidc"

  name                = "id-${var.project_name}-github-images-${var.environment_name}"
  resource_group_name = azurerm_resource_group.app.name
  location            = azurerm_resource_group.app.location
  subjects = {
    environment = {
      name    = "github-${var.github_environment_name}-images"
      subject = local.github_subject_env
    }
  }
  role_assignments = {
    tfstate = {
      scope                = module.state_backend.storage_account_id
      role_definition_name = "Storage Blob Data Reader"
    }
    app_reader = {
      scope                = azurerm_resource_group.app.id
      role_definition_name = "Reader"
    }
  }
  tags = merge(local.common_tags, { responsibility = "container-image-push" })
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

moved {
  from = azurerm_storage_account.tfstate
  to   = module.state_backend.azurerm_storage_account.this
}

moved {
  from = azurerm_storage_container.tfstate
  to   = module.state_backend.azurerm_storage_container.this
}

moved {
  from = azurerm_user_assigned_identity.github_actions
  to   = module.github_apply.azurerm_user_assigned_identity.this
}

moved {
  from = azurerm_federated_identity_credential.github_environment
  to   = module.github_apply.azurerm_federated_identity_credential.this["environment"]
}

moved {
  from = azurerm_federated_identity_credential.github_main_branch
  to   = module.github_apply.azurerm_federated_identity_credential.this["branch"]
}

moved {
  from = azurerm_role_assignment.github_tfstate_blob_contributor
  to   = module.github_apply.azurerm_role_assignment.this["tfstate"]
}

moved {
  from = azurerm_role_assignment.github_app_contributor
  to   = module.github_apply.azurerm_role_assignment.this["app_contributor"]
}

moved {
  from = azurerm_role_assignment.github_app_rbac_administrator
  to   = module.github_apply.azurerm_role_assignment.this["app_rbac"]
}

moved {
  from = azurerm_user_assigned_identity.github_plan
  to   = module.github_plan.azurerm_user_assigned_identity.this
}

moved {
  from = azurerm_federated_identity_credential.github_plan_pull_request
  to   = module.github_plan.azurerm_federated_identity_credential.this["pull_request"]
}

moved {
  from = azurerm_federated_identity_credential.github_plan_main_branch
  to   = module.github_plan.azurerm_federated_identity_credential.this["branch"]
}

moved {
  from = azurerm_role_assignment.github_plan_tfstate_reader
  to   = module.github_plan.azurerm_role_assignment.this["tfstate"]
}

moved {
  from = azurerm_role_assignment.github_plan_app_reader
  to   = module.github_plan.azurerm_role_assignment.this["app_reader"]
}

moved {
  from = azurerm_user_assigned_identity.github_image_push
  to   = module.github_image_push.azurerm_user_assigned_identity.this
}

moved {
  from = azurerm_federated_identity_credential.github_image_environment
  to   = module.github_image_push.azurerm_federated_identity_credential.this["environment"]
}

moved {
  from = azurerm_role_assignment.github_image_tfstate_reader
  to   = module.github_image_push.azurerm_role_assignment.this["tfstate"]
}

moved {
  from = azurerm_role_assignment.github_image_app_reader
  to   = module.github_image_push.azurerm_role_assignment.this["app_reader"]
}
