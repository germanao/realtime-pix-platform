resource "random_uuid" "workbook" {}

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "PerGB2018"
  retention_in_days   = var.retention_days
  tags                = var.tags
}

resource "azurerm_application_insights" "this" {
  name                = "appi-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"
  tags                = var.tags
}

resource "azurerm_application_insights_workbook" "showcase" {
  name                = random_uuid.workbook.result
  resource_group_name = var.resource_group_name
  location            = var.location
  display_name        = "Realtime PIX Showcase"
  tags                = var.tags

  data_json = jsonencode({
    version = "Notebook/1.0"
    items = [
      {
        type = 1
        name = "title"
        content = {
          json = "# Realtime PIX Platform\nOperational view for API health, Saga transitions, event flow, failures, and capacity."
        }
      },
      {
        type = 3
        name = "requests"
        content = {
          version      = "KqlItem/1.0"
          title        = "HTTP requests by service"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          query        = "requests | summarize Requests=count(), Failed=countif(success == false) by cloud_RoleName, bin(timestamp, 5m) | order by timestamp desc"
          size         = 0
        }
      },
      {
        type = 3
        name = "saga-outcomes"
        content = {
          version      = "KqlItem/1.0"
          title        = "Saga outcomes and compensation"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          query        = "traces | where message has_any ('PixTransferCompleted', 'PixTransferFailed', 'PixTransferCompensated', 'manual_intervention') | summarize Count=count() by cloud_RoleName, bin(timestamp, 5m)"
          size         = 0
        }
      },
      {
        type = 3
        name = "outbox-failures"
        content = {
          version      = "KqlItem/1.0"
          title        = "Outbox publish failures"
          queryType    = 0
          resourceType = "microsoft.insights/components"
          query        = "traces | where message has 'outbox' and severityLevel >= 2 | project timestamp, cloud_RoleName, message"
          size         = 0
        }
      }
    ]
    styleSettings = {}
  })
}

resource "azurerm_monitor_action_group" "this" {
  name                = "ag-${var.name_prefix}"
  resource_group_name = var.resource_group_name
  short_name          = "rtpix"
  tags                = var.tags

  email_receiver {
    name                    = "publisher"
    email_address           = var.publisher_email
    use_common_alert_schema = true
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "outbox_failures" {
  name                 = "alert-${var.alert_name_prefix}-outbox-failures-${var.name_suffix}"
  resource_group_name  = var.resource_group_name
  location             = var.location
  scopes               = [azurerm_log_analytics_workspace.this.id]
  description          = "Outbox dispatcher failures were detected in application logs."
  severity             = 2
  enabled              = true
  evaluation_frequency = "PT5M"
  window_duration      = "PT15M"
  tags                 = var.tags

  criteria {
    query                   = "AppTraces | where Message has 'outbox' and SeverityLevel >= 2"
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = 0
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "api_failures" {
  name                 = "alert-${var.alert_name_prefix}-api-failures-${var.name_suffix}"
  resource_group_name  = var.resource_group_name
  location             = var.location
  scopes               = [azurerm_log_analytics_workspace.this.id]
  description          = "HTTP API failures were detected in Application Insights request telemetry."
  severity             = 2
  enabled              = true
  evaluation_frequency = "PT5M"
  window_duration      = "PT15M"
  tags                 = var.tags

  criteria {
    query                   = "AppRequests | where Success == false"
    time_aggregation_method = "Count"
    operator                = "GreaterThan"
    threshold               = var.api_failure_threshold
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}
