# Independently Deployed Services over a Shared Database: The Woundtech EMR Example

## Context

The Woundtech EMR runs its services on **AWS ECS**. Each service is provisioned, deployed and scaled independently, while all services share one data store.

By the deployment definition, that is **microservices**: each service is a separate ECS task with its own release and scaling. The **shared database** is what decides whether the system gets microservices' *benefits* or drifts into a **distributed monolith**.

## What the scheduling-service code shows

- The scheduling-service does not read SQL tables directly. It calls a **HAPI FHIR server over HTTP** (`FHIR_API_URL`), and the FHIR server stores the data.
- It also has **its own MongoDB** (`MONGODB_URI`, `ENCOUNTERDURATION_MONGODB_URI`) for service-specific data.

So the "one database" is most likely **a shared FHIR repository acting as the clinical system of record**, with the services as domain facades over it. That is a recognized healthcare pattern, and meaningfully better than services sharing raw database tables.

| Pattern | Coupling |
|---|---|
| Services read and write each other's **tables** directly | **High.** A schema change breaks everyone. This is the classic distributed monolith |
| Services share a **FHIR server** through its API | **Moderate.** The coupling is to a *standard, versioned* data model (FHIR R4), not internal tables |
| Each service owns its **own database** | **Low.** Textbook microservices |

## What this setup gains

- **Independent deploy and scale.** For example, scheduling can scale for morning route planning without scaling patient or billing services.
- **Fault isolation at the compute level.** One crashing service does not take down the others.
- **One canonical clinical record.** For an EMR, a single FHIR store makes consistency, auditing and interoperability (Cerner, eClinicalWorks, payers) much easier than reconciling data split across many databases.

## What it gives up

1. **The shared store is a single point of failure and a bottleneck.** If the FHIR server or its database slows down, **every** service slows down. Independent ECS scaling does not help when the shared data tier is the constraint.
2. **Coupling through shared resources.** Services that write the same FHIR resources (Appointment, Encounter, Patient) can **overwrite each other's changes**, and no single owner enforces business rules. In DDD terms, aggregate boundaries are not enforced by service ownership.
3. **Coordinated releases.** A change to how one service uses a resource (a new extension, a changed status code, a new search parameter) can quietly break the others, pushing teams back toward releasing together.
4. **Security blast radius.** If every service has broad read and write access to the whole clinical record, one compromised service exposes all the PHI. That is a HIPAA minimum-necessary concern.
5. **Coupled schema evolution.** Upgrading the FHIR server or its version (R4 to R5), or re-indexing, affects every service at once.

## How to keep the benefits without the downsides

1. **Assign an owner to each resource type.** Only one service *writes* a given resource (for example, Scheduling owns Appointment and Slot). Others read it, or request changes through the owner's API or events. This restores aggregate boundaries without splitting the database.
2. **Scope access per service.** Use FHIR server authorization (SMART-style scopes or per-service credentials) so each service can access only the resource types it needs.
3. **Use optimistic concurrency.** FHIR supports versioned updates (`If-Match` with the resource version), which prevents lost updates when two services touch the same resource.
4. **Use events instead of polling the shared store.** FHIR Subscriptions, or SQS/SNS (the service already has SQS consumers and producers), let services react to changes without coupling through the database.
5. **Isolate the data tier's capacity.** Read replicas for read-heavy services, caching, and capacity monitoring on the FHIR server, since it is now the true scaling limit.
6. **Version the contracts.** Treat FHIR profiles and extensions as shared contracts, with a review process and contract tests.

## Bottom line

Independently deployed services over one shared FHIR store is a **pragmatic middle ground**. It is common and defensible for an EMR, and better than sharing tables. But the **data tier is still a monolith**. Its value depends on enforcing **single-writer ownership, scoped access and concurrency control**. Without those, it behaves like a distributed monolith, with microservices' operational cost and a monolith's coupling.
