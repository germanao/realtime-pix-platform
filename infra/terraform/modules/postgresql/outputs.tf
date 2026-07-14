output "id" { value = azurerm_postgresql_flexible_server.this.id }
output "name" { value = azurerm_postgresql_flexible_server.this.name }
output "fqdn" { value = azurerm_postgresql_flexible_server.this.fqdn }
output "database_ids" { value = { for name, database in azurerm_postgresql_flexible_server_database.this : name => database.id } }
