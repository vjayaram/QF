# Service Bus Premium (private endpoints, isolation). Entra-only: SAS keys are disabled.
variable "name" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "capacity" { type = number }
variable "topics" { type = set(string) }
variable "subscriptions" {
  description = "Subscriptions keyed by name: topic plus the message Subjects it wants."
  type = map(object({
    topic    = string
    subjects = list(string)
  }))
}
variable "log_analytics_workspace_id" { type = string }
variable "private_endpoint_subnet_id" { type = string }
variable "private_dns_zone_id" { type = string }
variable "action_group_id" { type = string }
variable "tags" { type = map(string) }

resource "azurerm_servicebus_namespace" "this" {
  name                          = "sb-${var.name}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = "Premium"
  capacity                      = var.capacity
  premium_messaging_partitions  = 1
  local_auth_enabled            = false
  public_network_access_enabled = false
  minimum_tls_version           = "1.2"
  tags                          = var.tags
}

resource "azurerm_servicebus_topic" "this" {
  for_each     = var.topics
  name         = each.key
  namespace_id = azurerm_servicebus_namespace.this.id

  # Outbox relay sets MessageId = event id; the broker drops replays inside this window.
  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = "PT1H"
  default_message_ttl                     = "P14D"
}

resource "azurerm_servicebus_subscription" "this" {
  for_each                             = var.subscriptions
  name                                 = each.key
  topic_id                             = azurerm_servicebus_topic.this[each.value.topic].id
  max_delivery_count                   = 10
  lock_duration                        = "PT1M"
  dead_lettering_on_message_expiration = true
}

resource "azurerm_servicebus_subscription_rule" "subjects" {
  for_each        = var.subscriptions
  name            = "subjects"
  subscription_id = azurerm_servicebus_subscription.this[each.key].id
  filter_type     = "SqlFilter"
  sql_filter      = "sys.Label IN (${join(", ", [for s in each.value.subjects : "'${s}'"])})"
}

module "pe" {
  source              = "../private_endpoint"
  name                = "sb-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id
  resource_id         = azurerm_servicebus_namespace.this.id
  subresource         = "namespace"
  private_dns_zone_id = var.private_dns_zone_id
  tags                = var.tags
}

resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "sb-to-log-analytics"
  target_resource_id         = azurerm_servicebus_namespace.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}

# A dead-lettered integration event means a context is out of sync (e.g. an appointment still points
# at a merged client). Page someone.
resource "azurerm_monitor_metric_alert" "dead_letters" {
  name                = "alert-${var.name}-sb-deadletters"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_servicebus_namespace.this.id]
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  description         = "Messages were dead-lettered on the integration bus."
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = 0
  }

  action {
    action_group_id = var.action_group_id
  }
}

output "namespace_id" { value = azurerm_servicebus_namespace.this.id }
output "fqdn" { value = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net" }
output "topic_ids" { value = { for k, t in azurerm_servicebus_topic.this : k => t.id } }
output "subscription_ids" { value = { for k, s in azurerm_servicebus_subscription.this : k => s.id } }
