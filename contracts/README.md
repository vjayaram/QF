# API contracts

The contracts other teams and systems build against, and the tooling that keeps them honest in CI.

| Contract | File | Gate in `pipelines/azure-pipelines.yml` |
|---|---|---|
| Public GraphQL schema (Fusion gateway) | `graphql/composed-schema.graphql` (generated, committed) | Build job: snapshot must be committed; GraphQL Inspector fails on breaking changes against `main` |
| Integration events (Service Bus) | `asyncapi/qf-integration-events.asyncapi.yaml` (hand-written) | Contracts job: Spectral house rules; `check-event-contracts.mjs` keeps the spec, `Qf.Contracts` and Terraform in sync |

Tool versions are pinned in `package.json` / `package-lock.json`: Spectral 6.17.1, GraphQL Inspector 7.0.2 (both Apache 2.0).

## Changing the GraphQL schema

```bash
pwsh ./scripts/compose-gateway.ps1            # recomposes the gateway and rewrites graphql/composed-schema.graphql
npm ci --prefix contracts
bash contracts/scripts/check-graphql-breaking.sh
```

Commit the updated snapshot with the code change; reviewers see the public schema diff in the pull request.

Breaking changes (removing or renaming a field, changing a type, adding a required argument) fail the build.
Use expand, migrate, contract instead:

1. Add the new field and mark the old one `@deprecated(reason: "...")`. Merge.
2. Move consumers to the new field.
3. Remove the deprecated field. Removing a field that `main` already deprecates is not treated as breaking.

When a break is unavoidable and consumers have agreed, queue the build with `allowBreakingGraphQLChanges=true`.

## Changing an integration event

The AsyncAPI document describes every event on every topic, who sends it and who receives it.

* **Adding an optional field:** add it to the record in `src/Contracts/Qf.Contracts` and to the schema here.
* **Anything breaking** (removing or renaming a field, changing a type, making a field required): publish a new
  version (`ClientMerged.v2`) as a new record, `Subjects` constant and AsyncAPI message, run both versions until
  every subscriber has moved, then retire `.v1`.
* **A new subscriber:** add the Terraform subscription and a `receive` operation here.

```bash
npm ci --prefix contracts
npm run lint:events --prefix contracts     # Spectral: AsyncAPI validity + house rules
npm run check:events --prefix contracts    # spec <-> Qf.Contracts records, Subjects, Topics, Terraform subscriptions
```

House rules (`.spectral.yaml`): versioned message names, `x-phi: false` on every message, no PHI-like property
names, every payload composes `IntegrationEvent` (eventId, occurredAt, tenantId) and maps to a C# record via
`x-csharp-type`, every message carries the Service Bus envelope trait, and descriptions on every property and
operation.
