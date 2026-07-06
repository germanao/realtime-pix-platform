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

variable "github_environment_name" {
  description = "GitHub environment allowed to use Azure OIDC."
  type        = string
  default     = "poc"
}

variable "github_branch" {
  description = "Branch allowed to run trusted applies."
  type        = string
  default     = "main"
}

variable "monthly_budget_amount" {
  description = "Monthly Azure budget amount in USD. Set to 0 to skip the budget."
  type        = number
  default     = 50
}

variable "budget_contact_emails" {
  description = "Emails that receive Azure budget alerts."
  type        = list(string)
  default     = []
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
