environment     = "prod"
subscription_id = "00000000-0000-0000-0000-000000000000" # placeholder: prod subscription
location        = "eastus2"
address_space   = "10.50.0.0/16"

sql_sku             = "BC_Gen5_4" # Business Critical: local HA replicas, read scale-out
sql_zone_redundant  = true
aca_zone_redundant  = true
servicebus_capacity = 2
replicas            = { min = 3, max = 20 }

sql_admin_group = {
  display_name = "sg-qf-ehr-prod-sql-admins"
  object_id    = "00000000-0000-0000-0000-000000000000"
}

entra = {
  tenant_id     = "00000000-0000-0000-0000-000000000000"
  api_client_id = "00000000-0000-0000-0000-000000000000"
  api_audience  = "api://qf-ehr-platform"
}

apim = {
  sku_name        = "Premium_1"
  publisher_name  = "Qualifacts Platform Engineering"
  publisher_email = "platform-engineering@example.com"
}

cors_allowed_origins     = ["https://ehr.example.com"]
alert_email              = "platform-oncall@example.com"
phi_audit_retention_days = 2190
