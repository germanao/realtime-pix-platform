variable "name_prefix" { type = string }
variable "alert_name_prefix" { type = string }
variable "name_suffix" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "publisher_email" { type = string }
variable "retention_days" {
  type    = number
  default = 30
}
variable "daily_quota_gb" {
  description = "Hard daily Log Analytics ingestion cap."
  type        = number
  default     = 0.1
}
variable "log_alerts_enabled" {
  description = "Log-query alerts have a separate charge and are disabled in the free-tier profile."
  type        = bool
  default     = false
}
variable "api_failure_threshold" {
  type    = number
  default = 5
}
variable "tags" {
  type    = map(string)
  default = {}
}
