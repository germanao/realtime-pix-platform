resource "azurerm_servicebus_namespace" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = var.namespace.sku
  capacity                      = try(var.namespace.capacity, null)
  premium_messaging_partitions  = try(var.namespace.premium_partitions, null)
  local_auth_enabled            = false
  minimum_tls_version           = "1.2"
  public_network_access_enabled = var.namespace.public_network_access
  tags                          = var.tags
}

resource "azurerm_servicebus_topic" "events" {
  name                                    = var.topic.name
  namespace_id                            = azurerm_servicebus_namespace.this.id
  partitioning_enabled                    = var.topic.partitioning_enabled
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = var.topic.duplicate_detection_window
  default_message_ttl                     = var.topic.default_message_ttl
}

resource "azurerm_servicebus_queue" "commands" {
  for_each                                = var.command_queues
  name                                    = each.key
  namespace_id                            = azurerm_servicebus_namespace.this.id
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = each.value.duplicate_detection_window
  dead_lettering_on_message_expiration    = true
  default_message_ttl                     = each.value.default_message_ttl
  max_delivery_count                      = each.value.max_delivery_count
}

resource "azurerm_servicebus_subscription" "consumers" {
  for_each            = var.subscriptions
  name                = each.key
  topic_id            = azurerm_servicebus_topic.events.id
  max_delivery_count  = 10
  default_message_ttl = "P14D"
}

resource "azurerm_servicebus_subscription_rule" "filters" {
  for_each        = var.subscriptions
  name            = "event-type-filter"
  subscription_id = azurerm_servicebus_subscription.consumers[each.key].id
  filter_type     = "SqlFilter"
  sql_filter      = each.value
}
