data "azurerm_monitor_diagnostic_categories" "this" {
  resource_id = var.target_resource_id
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                           = var.name
  target_resource_id             = var.target_resource_id
  log_analytics_workspace_id     = var.log_analytics_workspace_id
  log_analytics_destination_type = "Dedicated"

  dynamic "enabled_log" {
    for_each = setsubtract(
      toset(data.azurerm_monitor_diagnostic_categories.this.log_category_types),
      var.excluded_log_categories
    )
    content {
      category = enabled_log.value
    }
  }

  dynamic "enabled_metric" {
    for_each = var.enable_metrics ? toset(data.azurerm_monitor_diagnostic_categories.this.metrics) : toset([])
    content {
      category = enabled_metric.value
    }
  }
}
