variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "address_space" { type = list(string) }
variable "subnets" {
  type = map(object({
    address_prefixes  = list(string)
    delegation_name   = optional(string)
    service_endpoints = optional(list(string), [])
  }))
}
variable "private_dns_zones" {
  type    = set(string)
  default = []
}
variable "tags" {
  type    = map(string)
  default = {}
}
