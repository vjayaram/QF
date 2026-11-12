data "azurerm_client_config" "current" {}

locals {
  name         = "${var.prefix}-${var.environment}"
  compact_name = replace(local.name, "-", "")
  tags = merge(var.tags, {
    environment         = var.environment
    workload            = "ehr-platform"
    data_classification = "phi"
    managed_by          = "terraform"
  })

  # One entry per deployable. Adding a bounded context = adding an entry here + a model + a database project.
  apps = {
    clientregistry = {
      database      = "ClientRegistry"
      external      = false
      publishes     = ["client-registry-events"]
      subscriptions = []
    }
    scheduling = {
      database      = "Scheduling"
      external      = false
      publishes     = ["scheduling-events"]
      subscriptions = ["scheduling"]
    }
    gateway = {
      database      = null
      external      = true
      publishes     = []
      subscriptions = []
    }
  }

  databases = toset([for a in local.apps : a.database if a.database != null])
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

module "network" {
  source              = "./modules/network"
  name                = local.name
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  address_space       = var.address_space
  tags                = local.tags
}

module "monitoring" {
  source                   = "./modules/monitoring"
  name                     = local.name
  location                 = var.location
  resource_group_name      = azurerm_resource_group.this.name
  alert_email              = var.alert_email
  phi_audit_retention_days = var.phi_audit_retention_days
  tags                     = local.tags
}

# Workload identities: one user-assigned managed identity per app, least privilege, no secrets.
# Names are environment-qualified because SQL resolves "CREATE USER ... FROM EXTERNAL PROVIDER" by display name.
resource "azurerm_user_assigned_identity" "app" {
  for_each            = local.apps
  name                = "id-${local.name}-${each.key}"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

module "data" {
  source                     = "./modules/data"
  name                       = local.name
  compact_name               = local.compact_name
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  databases                  = local.databases
  sql_sku                    = var.sql_sku
  sql_zone_redundant         = var.sql_zone_redundant
  sql_admin_group            = var.sql_admin_group
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  public_registry_access     = var.public_registry_access
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  private_endpoint_subnet_id = module.network.private_endpoint_subnet_id
  private_dns_zone_ids       = module.network.private_dns_zone_ids
  secrets = {
    "appinsights-connection-string" = module.monitoring.app_insights_connection_string
  }
  tags = local.tags
}

module "messaging" {
  source              = "./modules/messaging"
  name                = local.name
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  capacity            = var.servicebus_capacity
  topics              = toset(["client-registry-events", "scheduling-events"])
  subscriptions = {
    scheduling = { topic = "client-registry-events", subjects = ["ClientMerged.v1"] }
    billing    = { topic = "scheduling-events", subjects = ["AppointmentCompleted.v1"] }
  }
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  private_endpoint_subnet_id = module.network.private_endpoint_subnet_id
  private_dns_zone_id        = module.network.private_dns_zone_ids["servicebus"]
  action_group_id            = module.monitoring.action_group_id
  tags                       = local.tags
}

# ---------- RBAC for workload identities ----------
resource "azurerm_role_assignment" "acr_pull" {
  for_each             = local.apps
  scope                = module.data.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}

resource "azurerm_role_assignment" "kv_secrets_user" {
  for_each             = local.apps
  scope                = module.data.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
}

resource "azurerm_role_assignment" "sb_sender" {
  for_each = merge([
    for app, cfg in local.apps : { for t in cfg.publishes : "${app}-${t}" => { app = app, topic = t } }
  ]...)
  scope                = module.messaging.topic_ids[each.value.topic]
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azurerm_user_assigned_identity.app[each.value.app].principal_id
}

resource "azurerm_role_assignment" "sb_receiver" {
  for_each = merge([
    for app, cfg in local.apps : { for s in cfg.subscriptions : "${app}-${s}" => { app = app, subscription = s } }
  ]...)
  scope                = module.messaging.subscription_ids[each.value.subscription]
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_user_assigned_identity.app[each.value.app].principal_id
}

# ---------- Compute ----------
module "platform" {
  source                     = "./modules/container_platform"
  name                       = local.name
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  subnet_id                  = module.network.aca_subnet_id
  vnet_id                    = module.network.vnet_id
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  zone_redundant             = var.aca_zone_redundant
  tags                       = local.tags
}

module "app" {
  source   = "./modules/container_app"
  for_each = local.apps

  name                = "ca-${each.key}"
  resource_group_name = azurerm_resource_group.this.name
  environment_id      = module.platform.id
  identity_id         = azurerm_user_assigned_identity.app[each.key].id
  identity_client_id  = azurerm_user_assigned_identity.app[each.key].client_id
  acr_login_server    = module.data.acr_login_server
  image               = "${module.data.acr_login_server}/${each.key}:${var.image_tag}"
  external            = each.value.external
  min_replicas        = var.replicas.min
  max_replicas        = var.replicas.max
  action_group_id     = module.monitoring.action_group_id
  tags                = local.tags

  env = merge(
    {
      ASPNETCORE_ENVIRONMENT = "Production"
      AzureAd__TenantId      = var.entra.tenant_id
      AzureAd__ClientId      = var.entra.api_client_id
      AzureAd__Audience      = var.entra.api_audience
    },
    each.value.database == null ? {} : {
      # Passwordless: the app's managed identity authenticates to Azure SQL.
      "ConnectionStrings__${each.value.database}" = "Server=tcp:${module.data.sql_server_fqdn},1433;Database=${each.value.database};Authentication=Active Directory Managed Identity;User Id=${azurerm_user_assigned_identity.app[each.key].client_id};Encrypt=True;TrustServerCertificate=False;"
      ServiceBus__FullyQualifiedNamespace         = module.messaging.fqdn
    },
    each.key == "gateway" ? { for i, o in var.cors_allowed_origins : "Cors__AllowedOrigins__${i}" => o } : {},
  )

  secret_env = {
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.data.key_vault_secret_ids["appinsights-connection-string"]
  }

  depends_on = [azurerm_role_assignment.acr_pull, azurerm_role_assignment.kv_secrets_user]
}

module "apim" {
  source                     = "./modules/apim"
  name                       = local.name
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  sku_name                   = var.apim.sku_name
  publisher_name             = var.apim.publisher_name
  publisher_email            = var.apim.publisher_email
  subnet_id                  = module.network.apim_subnet_id
  gateway_fqdn               = module.app["gateway"].fqdn
  entra_tenant_id            = var.entra.tenant_id
  api_audience               = var.entra.api_audience
  cors_allowed_origins       = var.cors_allowed_origins
  graphql_schema_path        = var.graphql_schema_path
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = local.tags
}
