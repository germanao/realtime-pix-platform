variable "name" {
  description = "Diagnostic setting name, unique on the target resource."
  type        = string
}

variable "target_resource_id" {
  description = "Azure resource ID that emits diagnostics."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Destination Log Analytics workspace resource ID."
  type        = string
}

variable "excluded_log_categories" {
  description = "Supported log categories intentionally excluded for cost, privacy, or noise reasons."
  type        = set(string)
  default     = []
}

variable "enable_metrics" {
  description = "Send all metrics supported by the target to Log Analytics."
  type        = bool
  default     = true
}
