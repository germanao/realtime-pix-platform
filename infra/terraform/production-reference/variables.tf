variable "location" {
  type    = string
  default = "brazilsouth"
}
variable "project_name" {
  type    = string
  default = "realtime-pix"
}
variable "environment_name" {
  type    = string
  default = "production"
  validation {
    condition     = var.environment_name == "production"
    error_message = "This root is reserved for the non-deployed production reference."
  }
}
variable "tenant_id" {
  description = "Microsoft Entra tenant ID for the separate production subscription."
  type        = string
}
variable "entra_admin_object_id" {
  description = "Object ID of the production database migration identity."
  type        = string
}
variable "entra_admin_name" {
  description = "Display name of the production database migration identity."
  type        = string
}
variable "publisher_email" { type = string }
variable "image_tag" {
  description = "Immutable image tag already present in the production ACR."
  type        = string
  default     = "reference-sha"
  validation {
    condition     = var.image_tag != "latest" && length(var.image_tag) > 6
    error_message = "Production images must use an immutable tag, never latest."
  }
}
variable "allowed_browser_origins" {
  type = list(string)
  validation {
    condition     = alltrue([for origin in var.allowed_browser_origins : !strcontains(origin, "*") && startswith(origin, "https://")])
    error_message = "Production origins must be exact HTTPS origins without wildcards."
  }
}
variable "tags" {
  type = map(string)
  default = {
    project     = "realtime-pix"
    environment = "production"
    managed_by  = "terraform"
    profile     = "reference-not-deployed"
  }
}
