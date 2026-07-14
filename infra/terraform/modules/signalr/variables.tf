variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "sku_name" { type = string }
variable "capacity" { type = number }
variable "public_network_access" { type = bool }
variable "allowed_origins" { type = list(string) }
variable "tags" {
  type    = map(string)
  default = {}
}
