resource "azurerm_api_management" "this" {
  name                 = var.name
  resource_group_name  = var.resource_group_name
  location             = var.location
  publisher_name       = var.publisher_name
  publisher_email      = var.publisher_email
  sku_name             = var.sku_name
  min_api_version      = "2022-08-01"
  virtual_network_type = var.outbound_subnet_id == null ? "None" : "External"
  tags                 = var.tags

  dynamic "virtual_network_configuration" {
    for_each = var.outbound_subnet_id == null ? [] : [var.outbound_subnet_id]
    content {
      subnet_id = virtual_network_configuration.value
    }
  }
}
