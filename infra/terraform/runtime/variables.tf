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
  description = "CORS origins allowed by public services."
  type        = list(string)
  default     = ["http://localhost:3000", "https://realtime-pix-web.vercel.app"]
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
