variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "alert_email" { type = string }
variable "phi_audit_retention_days" { type = number }
variable "tags" { type = map(string) }

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = 90
  # Logs carry no PHI values by design (redacted ToString, audit records hold identifiers only),
  # but the workspace is still treated as sensitive: RBAC-only access, no shared keys in apps.
  local_authentication_disabled = true
  tags                          = var.tags
}

# App traces include the "Qf.Audit.Phi" access-audit category; keep it for the HIPAA retention period.
resource "azurerm_log_analytics_workspace_table" "app_traces" {
  workspace_id            = azurerm_log_analytics_workspace.this.id
  name                    = "AppTraces"
  retention_in_days       = 90
  total_retention_in_days = var.phi_audit_retention_days
}

resource "azurerm_application_insights" "this" {
  name                          = "appi-${var.name}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  workspace_id                  = azurerm_log_analytics_workspace.this.id
  application_type              = "web"
  local_authentication_disabled = false # the OTel exporter uses the connection string; ingestion only
  internet_query_enabled        = false
  tags                          = var.tags
}

resource "azurerm_monitor_action_group" "oncall" {
  name                = "ag-${var.name}-oncall"
  resource_group_name = var.resource_group_name
  short_name          = "qfoncall"
  tags                = var.tags

  email_receiver {
    name                    = "oncall"
    email_address           = var.alert_email
    use_common_alert_schema = true
  }
}

output "log_analytics_workspace_id" { value = azurerm_log_analytics_workspace.this.id }
output "app_insights_connection_string" {
  value     = azurerm_application_insights.this.connection_string
  sensitive = true
}
output "action_group_id" { value = azurerm_monitor_action_group.oncall.id }
