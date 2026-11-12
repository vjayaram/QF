# Azure API Management: the single public entry point. Validates Entra ID tokens, rate-limits per
# agency, rejects over-deep or oversized GraphQL, then forwards privately to the Fusion gateway.
variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "sku_name" { type = string }
variable "publisher_name" { type = string }
variable "publisher_email" { type = string }
variable "subnet_id" { type = string }
variable "gateway_fqdn" { type = string }
variable "entra_tenant_id" { type = string }
variable "api_audience" { type = string }
variable "cors_allowed_origins" { type = list(string) }
variable "graphql_schema_path" { type = string }
variable "log_analytics_workspace_id" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_public_ip" "apim" {
  name                = "pip-${var.name}-apim"
  location            = var.location
  resource_group_name = var.resource_group_name
  allocation_method   = "Static"
  sku                 = "Standard"
  domain_name_label   = "apim-${var.name}"
  tags                = var.tags
}

resource "azurerm_api_management" "this" {
  name                 = "apim-${var.name}"
  location             = var.location
  resource_group_name  = var.resource_group_name
  publisher_name       = var.publisher_name
  publisher_email      = var.publisher_email
  sku_name             = var.sku_name
  virtual_network_type = "External"
  public_ip_address_id = azurerm_public_ip.apim.id
  tags                 = var.tags

  virtual_network_configuration {
    subnet_id = var.subnet_id
  }

  identity {
    type = "SystemAssigned"
  }

  protocols {
    http2_enabled = true
  }

  security {
    frontend_tls10_enabled = false
    frontend_tls11_enabled = false
    backend_tls10_enabled  = false
    backend_tls11_enabled  = false
  }
}

resource "azurerm_api_management_api" "graphql" {
  name                  = "ehr-graphql"
  resource_group_name   = var.resource_group_name
  api_management_name   = azurerm_api_management.this.name
  revision              = "1"
  display_name          = "EHR Platform GraphQL"
  path                  = "graphql"
  protocols             = ["https"]
  api_type              = "graphql"
  service_url           = "https://${var.gateway_fqdn}/graphql"
  subscription_required = false # identity is the Entra token; APIM subscription keys are not credentials
}

resource "azurerm_api_management_api_schema" "graphql" {
  count               = var.graphql_schema_path == "" ? 0 : 1
  api_name            = azurerm_api_management_api.graphql.name
  api_management_name = azurerm_api_management.this.name
  resource_group_name = var.resource_group_name
  schema_id           = "graphql"
  content_type        = "application/vnd.ms-azure-apim.graphql.schema"
  value               = file(var.graphql_schema_path)
}

resource "azurerm_api_management_api_policy" "graphql" {
  api_name            = azurerm_api_management_api.graphql.name
  api_management_name = azurerm_api_management.this.name
  resource_group_name = var.resource_group_name
  xml_content = templatefile("${path.module}/../../policies/graphql-api-policy.xml.tftpl", {
    tenant_id       = var.entra_tenant_id
    audience        = var.api_audience
    allowed_origins = var.cors_allowed_origins
  })
}

resource "azurerm_monitor_diagnostic_setting" "apim" {
  name                       = "apim-to-log-analytics"
  target_resource_id         = azurerm_api_management.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category = "GatewayLogs"
  }
}

output "gateway_url" { value = azurerm_api_management.this.gateway_url }
