variable "subscription_id" {
  type        = string
  description = "Target subscription (one subscription per environment)."
}

variable "environment" {
  type        = string
  description = "dev | test | prod"
  validation {
    condition     = contains(["dev", "test", "prod"], var.environment)
    error_message = "environment must be dev, test or prod."
  }
}

variable "location" {
  type    = string
  default = "eastus2"
}

variable "prefix" {
  type    = string
  default = "qf-ehr"
}

variable "address_space" {
  type        = string
  description = "VNet CIDR, e.g. 10.40.0.0/16. Must not overlap peered or on-prem ranges."
}

variable "image_tag" {
  type        = string
  description = "Initial image tag for new container apps. Later rollouts are done by the pipeline (revisions), not Terraform."
  default     = "bootstrap"
}

variable "sql_admin_group" {
  type = object({
    display_name = string
    object_id    = string
  })
  description = "Entra group that administers Azure SQL (contains the pipeline's deployment identity). Entra-only auth."
}

variable "sql_sku" {
  type        = string
  description = "e.g. GP_S_Gen5_2 (serverless) for dev, BC_Gen5_4 for prod."
}

variable "sql_zone_redundant" {
  type    = bool
  default = false
}

variable "servicebus_capacity" {
  type    = number
  default = 1
}

variable "aca_zone_redundant" {
  type    = bool
  default = false
}

variable "replicas" {
  type = object({
    min = number
    max = number
  })
  default = { min = 1, max = 5 }
}

variable "apim" {
  type = object({
    sku_name        = string
    publisher_name  = string
    publisher_email = string
  })
}

variable "entra" {
  type = object({
    tenant_id     = string
    api_client_id = string
    api_audience  = string
  })
  description = "Entra ID app registration that represents the platform API."
}

variable "graphql_schema_path" {
  type        = string
  description = "Composed Fusion schema (CI artifact) imported into APIM for request validation."
  default     = ""
}

variable "cors_allowed_origins" {
  type    = list(string)
  default = []
}

variable "alert_email" {
  type = string
}

variable "phi_audit_retention_days" {
  type        = number
  description = "Total retention of the PHI access audit table. HIPAA documentation retention is six years (2190 days)."
  default     = 2190
}

variable "public_registry_access" {
  type        = bool
  description = "Allow ACR access from outside the VNet. Keep false; CI agents run in the VNet (Managed DevOps Pools)."
  default     = false
}

variable "tags" {
  type    = map(string)
  default = {}
}
