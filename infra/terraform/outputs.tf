# Consumed by the release pipeline (terraform output -json).
output "resource_group_name" { value = azurerm_resource_group.this.name }
output "acr_login_server" { value = module.data.acr_login_server }
output "sql_server_fqdn" { value = module.data.sql_server_fqdn }
output "public_api_url" { value = "${module.apim.gateway_url}/graphql" }
output "container_apps" { value = { for k, m in module.app : k => m.name } }
output "app_identity_names" { value = { for k, i in azurerm_user_assigned_identity.app : k => i.name } }
output "databases" { value = { for k, a in local.apps : k => a.database if a.database != null } }
