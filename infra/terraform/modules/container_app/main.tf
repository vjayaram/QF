# One microservice. Terraform owns its shape (identity, scale, probes, config); the pipeline owns
# which image runs, by creating revisions and shifting traffic (blue/green and canary).
variable "name" { type = string }
variable "resource_group_name" { type = string }
variable "environment_id" { type = string }
variable "identity_id" { type = string }
variable "identity_client_id" { type = string }
variable "acr_login_server" { type = string }
variable "image" { type = string }
variable "external" {
  type        = bool
  description = "Reachable from the VNet (APIM). Subgraphs are environment-internal only."
}
variable "cpu" {
  type    = number
  default = 0.5
}
variable "memory" {
  type    = string
  default = "1Gi"
}
variable "min_replicas" { type = number }
variable "max_replicas" { type = number }
variable "env" { type = map(string) }
variable "secret_env" {
  type        = map(string)
  description = "ENV_NAME => Key Vault versionless secret id."
}
variable "action_group_id" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_container_app" "this" {
  name                         = var.name
  container_app_environment_id = var.environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Multiple"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.identity_id
  }

  dynamic "secret" {
    for_each = var.secret_env
    content {
      name                = lower(replace(secret.key, "_", "-"))
      key_vault_secret_id = secret.value
      identity            = var.identity_id
    }
  }

  ingress {
    external_enabled = var.external
    target_port      = 8080
    transport        = "http"
    # Subgraphs are called by app name inside the environment (mTLS between apps is on).
    allow_insecure_connections = !var.external

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = var.name
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      env {
        name  = "AZURE_CLIENT_ID"
        value = var.identity_client_id
      }

      dynamic "env" {
        for_each = var.env
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.secret_env
        content {
          name        = env.key
          secret_name = lower(replace(env.key, "_", "-"))
        }
      }

      startup_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health/live"
        interval_seconds        = 5
        failure_count_threshold = 12
      }

      liveness_probe {
        transport        = "HTTP"
        port             = 8080
        path             = "/health/live"
        interval_seconds = 15
      }

      readiness_probe {
        transport        = "HTTP"
        port             = 8080
        path             = "/health/ready"
        interval_seconds = 10
      }
    }

    http_scale_rule {
      name                = "http-concurrency"
      concurrent_requests = "50"
    }
  }

  lifecycle {
    # Image and traffic split are managed by the release pipeline (az containerapp revision ...).
    ignore_changes = [
      template[0].container[0].image,
      template[0].revision_suffix,
      ingress[0].traffic_weight,
    ]
  }
}

resource "azurerm_monitor_metric_alert" "server_errors" {
  name                = "alert-${var.name}-5xx"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_container_app.this.id]
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"
  description         = "${var.name} is returning 5xx responses."
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerApps"
    metric_name      = "Requests"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = 10

    dimension {
      name     = "statusCodeCategory"
      operator = "Include"
      values   = ["5xx"]
    }
  }

  action {
    action_group_id = var.action_group_id
  }
}

output "id" { value = azurerm_container_app.this.id }
output "name" { value = azurerm_container_app.this.name }
output "fqdn" { value = azurerm_container_app.this.ingress[0].fqdn }
