resource "azurerm_signalr_service" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  service_mode                  = "Default"
  public_network_access_enabled = var.public_network_access
  local_auth_enabled            = false
  connectivity_logs_enabled     = true
  messaging_logs_enabled        = true
  tags                          = var.tags

  sku {
    name     = var.sku_name
    capacity = var.capacity
  }

  cors {
    allowed_origins = var.allowed_origins
  }
}
