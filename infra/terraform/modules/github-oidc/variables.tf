variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }
variable "subjects" {
  description = "Stable credential key to Azure name and exact GitHub OIDC subject."
  type = map(object({
    name    = string
    subject = string
  }))
}
variable "role_assignments" {
  type = map(object({
    scope                = string
    role_definition_name = string
  }))
  default = {}
}
variable "tags" {
  type    = map(string)
  default = {}
}
