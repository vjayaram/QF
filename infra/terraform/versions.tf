terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.20"
    }
  }

  # State in Azure Storage, accessed with Entra ID (no storage keys). Values per environment:
  #   terraform init -backend-config=environments/<env>.backend.hcl
  backend "azurerm" {
    use_azuread_auth = true
    use_oidc         = true
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }

  # Azure DevOps workload identity federation: no client secrets anywhere in the pipeline.
  use_oidc            = true
  storage_use_azuread = true
  subscription_id     = var.subscription_id
}
