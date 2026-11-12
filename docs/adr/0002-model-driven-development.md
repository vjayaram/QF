# ADR 0002: Model-driven generation of structure, hand-written behaviour

**Status:** Accepted

**Decision.** A JSON model per bounded context (`models/*.model.json`) is the source of truth for structure:
value objects with constraints, enums with lifecycle transitions, aggregate state, domain events and PHI flags.
`Qf.ModelGen` validates the model and emits partial C# classes, EF Core mappings, GraphQL types and SQL DDL.
Behaviour (invariants, use cases) is written by hand in partial classes, which is where DDD's value lies.

**Why not UML/MDA tooling or a Roslyn source generator?** JSON is reviewable in pull requests and diffable. A
console generator with committed output keeps generated code visible and debuggable, works for SQL as well as C#,
and needs no IDE plug-in. CI fails on drift, so the commit is always consistent.

**Consequences.** Cross-cutting rules (PHI never in events, bounded strings, audit on PHI fields) are enforced once
in the generator instead of in reviews. Model changes that the generator does not support need a generator change.
