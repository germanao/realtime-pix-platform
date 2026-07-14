variable "name" {
  description = "Container App resource name."
  type        = string
}

variable "container_name" {
  description = "Container name inside the revision template."
  type        = string
}

variable "container_app_environment_id" {
  description = "Container Apps managed environment ID."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name."
  type        = string
}

variable "workload_profile_name" {
  description = "Optional dedicated workload profile. Null uses the environment's Consumption profile."
  type        = string
  default     = null
  nullable    = true
}

variable "identity_id" {
  description = "Dedicated user-assigned managed identity ID."
  type        = string
}

variable "registry" {
  description = "Private registry settings."
  type = object({
    server = string
  })
}

variable "image" {
  description = "Immutable image reference."
  type        = string

  validation {
    condition     = length(split(":", var.image)) > 1
    error_message = "Container images must include an explicit tag."
  }
}

variable "environment" {
  description = "Environment variables keyed by name. Use either value or secret_name."
  type = map(object({
    value       = optional(string)
    secret_name = optional(string)
  }))
  default = {}

  validation {
    condition = alltrue([
      for setting in values(var.environment) :
      (setting.value != null) != (setting.secret_name != null)
    ])
    error_message = "Each environment entry must set exactly one of value or secret_name."
  }
}

variable "secrets" {
  description = "Key Vault-backed Container App secrets keyed by secret name."
  type = map(object({
    key_vault_secret_id = string
  }))
  default = {}
}

variable "ingress" {
  description = "Ingress configuration. Null creates a worker without ingress."
  type = object({
    external_enabled       = bool
    target_port            = number
    transport              = optional(string, "auto")
    cors_allowed_origins   = optional(list(string), [])
    cors_allow_credentials = optional(bool, false)
  })
  default = null
}

variable "scale" {
  description = "Replica limits."
  type = object({
    min_replicas = number
    max_replicas = number
  })
}

variable "resources" {
  description = "Container CPU and memory allocation."
  type = object({
    cpu    = number
    memory = string
  })
  default = {
    cpu    = 0.25
    memory = "0.5Gi"
  }
}

variable "probes_enabled" {
  description = "Enable HTTP liveness and readiness probes."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Resource tags."
  type        = map(string)
  default     = {}
}
