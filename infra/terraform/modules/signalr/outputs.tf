output "id" { value = azurerm_signalr_service.this.id }
output "hostname" { value = azurerm_signalr_service.this.hostname }
output "endpoint" { value = "https://${azurerm_signalr_service.this.hostname}" }
