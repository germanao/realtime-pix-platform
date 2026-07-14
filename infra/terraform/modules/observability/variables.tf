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
variable "api_failure_threshold" {
  type    = number
  default = 5
}
variable "tags" {
  type    = map(string)
  default = {}
}
