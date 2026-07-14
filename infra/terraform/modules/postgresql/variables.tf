variable "name" {
  description = "Globally unique PostgreSQL Flexible Server name."
  type        = string
  validation {
    condition     = can(regex("^[a-z0-9-]{3,63}$", var.name))
    error_message = "name must contain 3-63 lowercase letters, digits, or hyphens."
  }
}

variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "tenant_id" { type = string }

variable "administrator" {
  description = "Microsoft Entra administrator. No local PostgreSQL administrator is created."
  type = object({
    entra_object_id = string
    entra_name      = string
    entra_type      = optional(string, "ServicePrincipal")
  })
}

variable "server" {
  type = object({
    version                = optional(string, "16")
    sku_name               = string
    storage_mb             = number
    backup_retention_days  = number
    geo_redundant_backup   = optional(bool, false)
    public_network_access  = bool
    delegated_subnet_id    = optional(string)
    private_dns_zone_id    = optional(string)
    high_availability_mode = optional(string)
    availability_zone      = optional(string)
    allow_azure_services   = optional(bool, false)
  })
  validation {
    condition     = var.server.backup_retention_days >= 7 && var.server.backup_retention_days <= 35
    error_message = "backup_retention_days must be between 7 and 35."
  }
  validation {
    condition = var.server.public_network_access || (
      try(var.server.delegated_subnet_id, null) != null &&
      try(var.server.private_dns_zone_id, null) != null
    )
    error_message = "Private servers require delegated_subnet_id and private_dns_zone_id."
  }
}

variable "databases" {
  description = "Service-owned databases created on this server."
  type        = set(string)
}

variable "tags" {
  type    = map(string)
  default = {}
}
