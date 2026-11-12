# Internal-only Container Apps environment. Nothing in it has a public IP; APIM is the only way in.
variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "subnet_id" { type = string }
variable "vnet_id" { type = string }
variable "log_analytics_workspace_id" { type = string }
variable "zone_redundant" { type = bool }
variable "tags" { type = map(string) }

resource "azurerm_container_app_environment" "this" {
  name                               = "cae-${var.name}"
  location                           = var.location
  resource_group_name                = var.resource_group_name
  log_analytics_workspace_id         = var.log_analytics_workspace_id
  infrastructure_subnet_id           = var.subnet_id
  infrastructure_resource_group_name = "rg-${var.name}-cae-infra"
  internal_load_balancer_enabled     = true
  zone_redundancy_enabled            = var.zone_redundant
  # Encrypts app-to-app traffic inside the environment (gateway -> subgraphs).
  mutual_tls_enabled = true
  tags               = var.tags

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

# The environment's internal domain must resolve inside the VNet (for APIM).
resource "azurerm_private_dns_zone" "env" {
  name                = azurerm_container_app_environment.this.default_domain
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_a_record" "wildcard" {
  name                = "*"
  zone_name           = azurerm_private_dns_zone.env.name
  resource_group_name = var.resource_group_name
  ttl                 = 300
  records             = [azurerm_container_app_environment.this.static_ip_address]
}

resource "azurerm_private_dns_zone_virtual_network_link" "env" {
  name                  = "link-cae"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.env.name
  virtual_network_id    = var.vnet_id
}

output "id" { value = azurerm_container_app_environment.this.id }
output "default_domain" { value = azurerm_container_app_environment.this.default_domain }
