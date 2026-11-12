environment     = "dev"
subscription_id = "00000000-0000-0000-0000-000000000000" # placeholder: dev subscription
location        = "eastus2"
address_space   = "10.40.0.0/16"

sql_sku            = "GP_S_Gen5_2" # serverless, auto-pause friendly
sql_zone_redundant = false
aca_zone_redundant = false
replicas           = { min = 1, max = 3 }

sql_admin_group = {
  display_name = "sg-qf-ehr-dev-sql-admins"
  object_id    = "00000000-0000-0000-0000-000000000000"
}

entra = {
  tenant_id     = "00000000-0000-0000-0000-000000000000"
  api_client_id = "00000000-0000-0000-0000-000000000000"
  api_audience  = "api://qf-ehr-platform"
}

apim = {
  sku_name        = "Developer_1"
  publisher_name  = "Qualifacts Platform Engineering"
  publisher_email = "platform-engineering@example.com"
}

cors_allowed_origins     = ["https://dev.ehr.example.com"]
alert_email              = "platform-oncall-dev@example.com"
phi_audit_retention_days = 90 # dev holds synthetic data only
