variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "publisher_name" { type = string }
variable "publisher_email" { type = string }
variable "sku_name" { type = string }
variable "outbound_subnet_id" {
  description = "Delegated subnet used for APIM outbound VNet integration."
  type        = string
  default     = null
  nullable    = true
}
variable "tags" {
  type    = map(string)
  default = {}
}
