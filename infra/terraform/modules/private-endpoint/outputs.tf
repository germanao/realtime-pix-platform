output "id" { value = azurerm_private_endpoint.this.id }
output "name" { value = azurerm_private_endpoint.this.name }
output "private_ip_address" { value = azurerm_private_endpoint.this.private_service_connection[0].private_ip_address }
