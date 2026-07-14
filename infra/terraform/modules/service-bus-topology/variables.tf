variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }

variable "namespace" {
  type = object({
    sku                   = string
    capacity              = optional(number)
    premium_partitions    = optional(number)
    public_network_access = bool
  })
  validation {
    condition     = contains(["Standard", "Premium"], var.namespace.sku)
    error_message = "Service Bus must use Standard or Premium because Topics are required."
  }
  validation {
    condition     = var.namespace.sku != "Premium" || coalesce(var.namespace.capacity, 0) > 0
    error_message = "Premium Service Bus requires a positive messaging-unit capacity."
  }
}

variable "topic" {
  type = object({
    name                       = string
    duplicate_detection_window = optional(string, "PT10M")
    default_message_ttl        = optional(string, "P14D")
    partitioning_enabled       = optional(bool, true)
  })
}

variable "command_queues" {
  type = map(object({
    duplicate_detection_window = optional(string, "PT10M")
    default_message_ttl        = optional(string, "P1D")
    max_delivery_count         = optional(number, 10)
  }))
}

variable "subscriptions" {
  description = "Topic subscription name to SQL filter expression. Never use a TrueFilter."
  type        = map(string)
  validation {
    condition = alltrue([
      for expression in values(var.subscriptions) : !contains(["1=1", "1 = 1", "true"], lower(trimspace(expression)))
    ])
    error_message = "Every subscription must have a restrictive SQL filter; TrueFilter expressions are forbidden."
  }
}

variable "tags" {
  type    = map(string)
  default = {}
}
