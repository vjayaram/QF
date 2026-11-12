# Qualifacts EHR modernization: reference implementation

End-to-end example of carving acquired, monolithic behavioral-health EHRs (CareLogic, Credible, InSync)
into domain-driven microservices on Azure, all on the Microsoft stack:

| Concern | Choice |
|---|---|
| Method | **DDD** (bounded contexts, aggregates, anti-corruption layer, published language) + **MDD** (JSON domain models generate code, mappings, GraphQL types and SQL) |
| Runtime | .NET 10 (LTS), ASP.NET Core, EF Core 10 |
| API | GraphQL: Hot Chocolate subgraphs composed by a Hot Chocolate **Fusion** gateway, fronted by **Azure API Management** |
| Data | **Azure SQL**, database per bounded context, schema as a SQL Database Project (DACPAC), row-level security per tenant |
| Messaging | **Azure Service Bus** Premium, transactional outbox, idempotent consumers |
| Compute | **Azure Container Apps** (internal environment, revisions for blue/green and canary) |
| Identity | **Microsoft Entra ID** for users (app roles + tenant claim) and workloads (managed identities; no secrets) |
| IaC / CI/CD | **Terraform** (azurerm 4.x) + **Azure DevOps** YAML (workload identity federation, Managed DevOps Pools) |
| Observability | OpenTelemetry to Application Insights / Log Analytics, PHI access audit |

The full design write-up is `docs/Qualifacts-EHR-Modernization-Design.docx`.

## Repository map

```
models/                     MDD source of truth (one JSON model per bounded context) + JSON schema
tools/Qf.ModelGen/          generator: validates models, emits *.g.cs and *.g.sql; --check for CI drift
src/BuildingBlocks/         DDD kernel (Result, AggregateRoot, events), EF/outbox/Service Bus, web hosting
src/Contracts/              published language: versioned integration events between contexts
contracts/                  API contracts + CI gates: GraphQL schema snapshot, AsyncAPI for events, Spectral rules
src/Services/ClientRegistry Domain · Application · AntiCorruption (legacy EHR translators) · Infrastructure · Api
src/Services/Scheduling     Domain · Application · Infrastructure (Service Bus consumer) · Api
src/Gateway/Qf.Gateway      Fusion gateway (gateway.fgp composed in CI)
database/<Context>/         SQL Database Projects: generated tables, outbox/inbox, RLS, identity grants
tests/                      unit, application, ACL, generator, architecture, API integration (Testcontainers)
infra/terraform/            VNet, private endpoints, Azure SQL, Service Bus, Key Vault, ACR, Container Apps, APIM, alerts
pipelines/                  Azure DevOps multi-stage pipeline + templates
scripts/                    generate, compose gateway, progressive rollout, smoke test, local DB
docs/                       design document, ADRs, diagrams (Mermaid sources + PNG)
```

## Working with the model (MDD loop)

1. Edit `models/<context>.model.json` (add a field, an enum value, a state transition, an event).
2. `pwsh ./scripts/generate.ps1` regenerates domain structure, EF mappings, GraphQL types and SQL tables.
3. Write behaviour in the hand-written partial class (for example `Client.cs`) and tests.
4. CI runs `Qf.ModelGen --all --check`; any hand edit to a generated file or a stale model fails the build.
5. If the change reaches the public GraphQL schema, run `scripts/compose-gateway.ps1` and commit the updated
   `contracts/graphql/composed-schema.graphql` (see API contracts below).

The generator refuses models that break architecture rules: PHI in a domain event, a string without a max
length, a transition to an undeclared state, a misspelled property, and others (see `ModelValidator.cs`).

## API contracts

The public GraphQL schema and the Service Bus events are versioned contracts with CI gates (see
`contracts/README.md` and ADR 0006):

* **GraphQL:** `scripts/compose-gateway.ps1` rewrites `contracts/graphql/composed-schema.graphql`; commit it with
  the change. CI fails on an uncommitted schema change or a breaking change against `main` (deprecate first).
* **Events:** `contracts/asyncapi/qf-integration-events.asyncapi.yaml` is linted by Spectral and checked against
  `Qf.Contracts` and the Terraform subscriptions on every build.

## Run locally

```bash
docker compose up -d                       # SQL Server + Service Bus emulator
pwsh ./scripts/local-db.ps1                # publish DACPACs
dotnet user-jwts create --project src/Services/ClientRegistry/Qf.ClientRegistry.Api \
  --audience api://qf-ehr-platform --role Clients.Read --role Clients.Write --role Phi.Read \
  --claim qf_tenant_id=a1a1a1a1-0000-4000-8000-000000000001
dotnet run --project src/Services/ClientRegistry/Qf.ClientRegistry.Api
```

## Test

```bash
dotnet test Qf.EhrPlatform.slnx --settings coverage.runsettings --collect "XPlat Code Coverage"
```

Integration tests need Docker (Testcontainers starts SQL Server and applies the database project scripts).

## First-time setup notes

* Package versions are pinned centrally in `Directory.Packages.props`. Run one `dotnet restore` and commit the
  generated `packages.lock.json` files; CI then restores in locked mode.
* Terraform placeholders (`00000000-...` ids, e-mail addresses) in `infra/terraform/environments/*.tfvars` must be
  replaced, and the state storage accounts in `*.backend.hcl` created once.
* Contract gates: run `pwsh ./scripts/compose-gateway.ps1` once and commit the generated
  `contracts/graphql/composed-schema.graphql`; the build fails until the snapshot is committed. Build agents need
  Node.js 22 (installed by the `UseNode@1` task in `pipelines/templates/contracts-setup.yml`).
* Azure DevOps: create the `qf-ehr-platform` variable group (`serviceConnectionDev/Prod`, `acrNameDev/Prod`),
  workload-identity service connections, a Managed DevOps Pool in `snet-agents`, and approvals on the
  `qf-ehr-prod` environment.

## What was verified while building this example

* The model generator, building blocks, both domains, application layers, contracts and the anti-corruption
  layer were compiled on .NET 10 with warnings as errors and the recommended analyzer set.
* 116 unit, application, translator, generator and architecture tests were executed and pass.
* Terraform files were parsed and every module call was cross-checked against module variables and outputs;
  pipeline YAML was parsed.
* Not executed in that environment (no NuGet, Docker or Azure access): the EF Core, Hot Chocolate, Service Bus and
  Entra ID adapters, the integration tests, `terraform validate/plan`, the DACPAC build and the pipeline itself.
  Expect a first pass to fix small API-version differences when you restore packages.
