resource "azurerm_container_app" "this" {
  name                         = var.name
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  workload_profile_name        = var.workload_profile_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.registry.server
    identity = var.identity_id
  }

  dynamic "secret" {
    for_each = var.secrets
    content {
      name                = secret.key
      key_vault_secret_id = secret.value.key_vault_secret_id
      identity            = var.identity_id
    }
  }

  dynamic "ingress" {
    for_each = var.ingress == null ? [] : [var.ingress]
    content {
      external_enabled           = ingress.value.external_enabled
      target_port                = ingress.value.target_port
      transport                  = ingress.value.transport
      allow_insecure_connections = false

      dynamic "cors" {
        for_each = length(ingress.value.cors_allowed_origins) == 0 ? [] : [ingress.value.cors_allowed_origins]
        content {
          allowed_origins           = cors.value
          allowed_methods           = ["GET", "POST", "OPTIONS"]
          allowed_headers           = ["*"]
          allow_credentials_enabled = ingress.value.cors_allow_credentials
        }
      }

      traffic_weight {
        percentage      = 100
        latest_revision = true
      }
    }
  }

  template {
    min_replicas = var.scale.min_replicas
    max_replicas = var.scale.max_replicas

    container {
      name   = var.container_name
      image  = var.image
      cpu    = var.resources.cpu
      memory = var.resources.memory

      dynamic "env" {
        for_each = var.environment
        content {
          name        = env.key
          value       = env.value.value
          secret_name = env.value.secret_name
        }
      }

      dynamic "liveness_probe" {
        for_each = var.probes_enabled ? [1] : []
        content {
          transport = "HTTP"
          port      = 8080
          path      = "/health/live"
        }
      }

      dynamic "readiness_probe" {
        for_each = var.probes_enabled ? [1] : []
        content {
          transport = "HTTP"
          port      = 8080
          path      = "/health/ready"
        }
      }
    }
  }

  lifecycle {
    precondition {
      condition     = var.scale.min_replicas <= var.scale.max_replicas
      error_message = "min_replicas cannot exceed max_replicas."
    }
  }
}
