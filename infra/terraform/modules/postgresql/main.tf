resource "azurerm_postgresql_flexible_server" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  version                       = var.server.version
  sku_name                      = var.server.sku_name
  storage_mb                    = var.server.storage_mb
  backup_retention_days         = var.server.backup_retention_days
  geo_redundant_backup_enabled  = var.server.geo_redundant_backup
  public_network_access_enabled = var.server.public_network_access
  delegated_subnet_id           = try(var.server.delegated_subnet_id, null)
  private_dns_zone_id           = try(var.server.private_dns_zone_id, null)
  zone                          = try(var.server.availability_zone, null)
  tags                          = var.tags

  identity {
    type = "SystemAssigned"
  }

  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = var.tenant_id
  }

  dynamic "high_availability" {
    for_each = try(var.server.high_availability_mode, null) == null ? [] : [var.server.high_availability_mode]
    content {
      mode = high_availability.value
    }
  }

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "this" {
  resource_group_name = var.resource_group_name
  server_name         = azurerm_postgresql_flexible_server.this.name
  tenant_id           = var.tenant_id
  object_id           = var.administrator.entra_object_id
  principal_name      = var.administrator.entra_name
  principal_type      = var.administrator.entra_type
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  count            = var.server.allow_azure_services ? 1 : 0
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_postgresql_flexible_server_database" "this" {
  for_each  = var.databases
  name      = each.value
  server_id = azurerm_postgresql_flexible_server.this.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}
