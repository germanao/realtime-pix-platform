variable "tfstate_resource_group_name" {
  description = "Terraform state resource group created by bootstrap."
  type        = string
}

variable "tfstate_storage_account_name" {
  description = "Terraform state storage account created by bootstrap."
  type        = string
}

variable "tfstate_container_name" {
  description = "Terraform state container created by bootstrap."
  type        = string
}

variable "project_name" {
  description = "Short name used for Azure resource names and tags."
  type        = string
  default     = "realtime-pix"
}

variable "environment_name" {
  description = "Environment name."
  type        = string
  default     = "poc"
}

variable "image_tag" {
  description = "Container image tag to deploy."
  type        = string
}

variable "app_config_label" {
  description = "Azure App Configuration label."
  type        = string
  default     = "poc"
}

variable "allowed_cors_origins" {
  description = "Exact CORS origins allowed by public services. Preview hosts are controlled separately."
  type        = list(string)
  default     = ["http://localhost:3000", "https://realtime-pix-web.vercel.app"]

  validation {
    condition = length(var.allowed_cors_origins) > 0 && alltrue([
      for origin in var.allowed_cors_origins : origin == "http://localhost:3000" ||
      can(regex("^https://[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:[0-9]{1,5})?$", origin))
    ])
    error_message = "Runtime CORS origins must contain at least one exact HTTPS host origin or http://localhost:3000; use allow_vercel_previews for previews."
  }
}

variable "allow_vercel_previews" {
  description = "Allow preview hosts belonging to the realtime-pix-web Vercel project."
  type        = bool
  default     = true
}

variable "vercel_preview_project_name" {
  description = "Vercel project-name component used in generated preview deployment hosts."
  type        = string
  default     = "realtime-pix"

  validation {
    condition     = can(regex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", var.vercel_preview_project_name))
    error_message = "vercel_preview_project_name must be a lowercase DNS label."
  }
}

variable "vercel_preview_scope_slug" {
  description = "Vercel account/team scope slug that owns the preview deployment hosts."
  type        = string
  default     = "germanaos-projects"

  validation {
    condition     = can(regex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", var.vercel_preview_scope_slug))
    error_message = "vercel_preview_scope_slug must be a lowercase DNS label."
  }
}

variable "tags" {
  description = "Tags applied to Azure resources."
  type        = map(string)
  default = {
    project     = "realtime-pix"
    environment = "poc"
    managed_by  = "terraform"
  }
}
