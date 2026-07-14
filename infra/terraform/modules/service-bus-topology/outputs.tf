output "namespace_id" { value = azurerm_servicebus_namespace.this.id }
output "namespace_name" { value = azurerm_servicebus_namespace.this.name }
output "fully_qualified_namespace" { value = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net" }
output "topic_id" { value = azurerm_servicebus_topic.events.id }
output "topic_name" { value = azurerm_servicebus_topic.events.name }
output "queue_ids" { value = { for name, queue in azurerm_servicebus_queue.commands : name => queue.id } }
output "subscription_ids" { value = { for name, subscription in azurerm_servicebus_subscription.consumers : name => subscription.id } }
