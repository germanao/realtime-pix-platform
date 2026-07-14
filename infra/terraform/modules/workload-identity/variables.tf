variable "name" {
  description = "Globally unique workload identity name within the resource group."
  type        = string

  validation {
    condition     = startswith(var.name, "id-")
    error_message = "Workload identity names must use the id- prefix."
  }
}

variable "resource_group_name" {
  description = "Resource group that owns the identity."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "role_assignments" {
  description = "Least-privilege RBAC assignments keyed by a stable logical name."
  type = map(object({
    scope                = string
    role_definition_name = string
  }))
  default = {}
}

variable "tags" {
  description = "Resource tags."
  type        = map(string)
  default     = {}
}
