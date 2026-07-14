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
  description = "Origins accepted by the POC Azure SignalR transport. Application endpoints enforce the narrower project allowlist."
  type        = list(string)
  default     = ["*"]

  validation {
    condition = alltrue([
      for origin in var.allowed_cors_origins :
      origin == "*" || (!strcontains(origin, "*") && (origin == "http://localhost:3000" || startswith(origin, "https://")))
    ])
    error_message = "SignalR origins must be exact origins or the explicit POC transport wildcard; partial wildcards are unsupported."
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
