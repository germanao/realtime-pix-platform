variable "location" {
  description = "Azure region for the POC."
  type        = string
  default     = "brazilsouth"
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

variable "postgres_admin_login" {
  description = "PostgreSQL administrator login."
  type        = string
  default     = "pixadmin"
}

variable "publisher_name" {
  description = "APIM publisher name."
  type        = string
  default     = "Realtime PIX POC"
}

variable "publisher_email" {
  description = "APIM publisher email."
  type        = string
}

variable "allowed_cors_origins" {
  description = "Browser origins allowed by Azure SignalR and APIM."
  type        = list(string)
  default     = ["http://localhost:3000", "https://realtime-pix-web.vercel.app", "https://realtime-pix-web*.vercel.app"]
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
