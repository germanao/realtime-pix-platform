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

variable "manage_vercel" {
  description = "Whether Terraform should manage/import the Vercel project and public environment variables."
  type        = bool
  default     = false
}

variable "vercel_api_token" {
  description = "Vercel API token. Required only when manage_vercel is true."
  type        = string
  default     = ""
  sensitive   = true
}

variable "vercel_team_id" {
  description = "Optional Vercel team id."
  type        = string
  default     = null
}

variable "vercel_project_name" {
  description = "Vercel project name."
  type        = string
  default     = "realtime-pix-web"
}

variable "github_owner" {
  description = "GitHub repository owner."
  type        = string
  default     = "germanao"
}

variable "github_repository" {
  description = "GitHub repository name."
  type        = string
  default     = "realtime-pix-platform"
}

variable "github_branch" {
  description = "Production branch for Vercel."
  type        = string
  default     = "main"
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
