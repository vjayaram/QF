# ADR 0004: Azure Container Apps, Azure SQL and Service Bus, private and passwordless

**Status:** Accepted

**Decision.** Container Apps (internal environment, workload profiles, revisions) instead of AKS for this scale:
less cluster operations, native blue/green and canary. Azure SQL database per context with Entra-only
authentication, row-level security keyed on `SESSION_CONTEXT('TenantId')`, TDE and long-term retention.
Service Bus Premium with duplicate detection; transactional outbox for publishing, inbox table for idempotency.
Every PaaS dependency is reached through private endpoints; every workload uses a user-assigned managed identity.

**Revisit when.** Workloads need service mesh features, GPU nodes or very high pod density: move to AKS. The
container images, Terraform modules for data and messaging, and the pipeline stages stay the same.
