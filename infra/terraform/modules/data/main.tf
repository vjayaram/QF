# Azure SQL (database per bounded context), Key Vault and Container Registry. All private, all Entra-only.
variable "name" { type = string }
variable "compact_name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "databases" { type = set(string) }
variable "sql_sku" { type = string }
variable "sql_zone_redundant" { type = bool }
variable "sql_admin_group" {
  type = object({
    display_name = string
    object_id    = string
  })
}
variable "tenant_id" { type = string }
variable "public_registry_access" { type = bool }
variable "log_analytics_workspace_id" { type = string }
variable "private_endpoint_subnet_id" { type = string }
variable "private_dns_zone_ids" { type = map(string) }
variable "secrets" {
  type      = map(string)
  sensitive = true
}
variable "tags" { type = map(string) }

# ---------- Azure SQL ----------
resource "azurerm_mssql_server" "this" {
  name                          = "sql-${var.name}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = false
  tags                          = var.tags

  azuread_administrator {
    login_username              = var.sql_admin_group.display_name
    object_id                   = var.sql_admin_group.object_id
    tenant_id                   = var.tenant_id
    azuread_authentication_only = true # no SQL logins or passwords exist
  }

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_mssql_database" "this" {
  for_each             = var.databases
  name                 = each.key
  server_id            = azurerm_mssql_server.this.id
  sku_name             = var.sql_sku
  zone_redundant       = var.sql_zone_redundant
  storage_account_type = "Geo"
  # TDE (encryption at rest) is on by default for Azure SQL and cannot be disabled here.
  tags = var.tags

  short_term_retention_policy {
    retention_days = 35
  }

  long_term_retention_policy {
    weekly_retention  = "P12W"
    monthly_retention = "P12M"
    yearly_retention  = "P7Y"
    week_of_year      = 1
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_mssql_server_extended_auditing_policy" "this" {
  server_id              = azurerm_mssql_server.this.id
  log_monitoring_enabled = true
}

resource "azurerm_monitor_diagnostic_setting" "sql_audit" {
  name                       = "audit-to-log-analytics"
  target_resource_id         = "${azurerm_mssql_server.this.id}/databases/master"
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category = "SQLSecurityAuditEvents"
  }
}

resource "azurerm_mssql_server_security_alert_policy" "this" {
  resource_group_name = var.resource_group_name
  server_name         = azurerm_mssql_server.this.name
  state               = "Enabled"
}

module "sql_pe" {
  source              = "../private_endpoint"
  name                = "sql-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id
  resource_id         = azurerm_mssql_server.this.id
  subresource         = "sqlServer"
  private_dns_zone_id = var.private_dns_zone_ids["sql"]
  tags                = var.tags
}

# ---------- Key Vault ----------
resource "azurerm_key_vault" "this" {
  name                          = "kv-${var.compact_name}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  tenant_id                     = var.tenant_id
  sku_name                      = "standard"
  enable_rbac_authorization     = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false
  tags                          = var.tags

  network_acls {
    default_action = "Deny"
    bypass         = "AzureServices"
  }
}

resource "azurerm_key_vault_secret" "this" {
  for_each     = nonsensitive(toset(keys(var.secrets)))
  name         = each.key
  value        = var.secrets[each.key]
  key_vault_id = azurerm_key_vault.this.id
  content_type = "text/plain"
}

module "kv_pe" {
  source              = "../private_endpoint"
  name                = "kv-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id
  resource_id         = azurerm_key_vault.this.id
  subresource         = "vault"
  private_dns_zone_id = var.private_dns_zone_ids["keyvault"]
  tags                = var.tags
}

resource "azurerm_monitor_diagnostic_setting" "kv" {
  name                       = "kv-to-log-analytics"
  target_resource_id         = azurerm_key_vault.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "audit"
  }
}

# ---------- Container Registry ----------
resource "azurerm_container_registry" "this" {
  name                          = "cr${var.compact_name}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = "Premium" # private endpoints, retention, zone redundancy
  admin_enabled                 = false
  public_network_access_enabled = var.public_registry_access
  network_rule_bypass_option    = "AzureServices"
  zone_redundancy_enabled       = var.sql_zone_redundant
  tags                          = var.tags

  retention_policy_in_days = 30
}

module "acr_pe" {
  source              = "../private_endpoint"
  name                = "acr-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id
  resource_id         = azurerm_container_registry.this.id
  subresource         = "registry"
  private_dns_zone_id = var.private_dns_zone_ids["acr"]
  tags                = var.tags
}

output "sql_server_fqdn" { value = azurerm_mssql_server.this.fully_qualified_domain_name }
output "key_vault_id" { value = azurerm_key_vault.this.id }
output "key_vault_secret_ids" { value = { for k, s in azurerm_key_vault_secret.this : k => s.versionless_id } }
output "acr_id" { value = azurerm_container_registry.this.id }
output "acr_login_server" { value = azurerm_container_registry.this.login_server }
