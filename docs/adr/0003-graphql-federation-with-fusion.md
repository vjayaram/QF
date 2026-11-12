# ADR 0003: GraphQL federation with Hot Chocolate Fusion behind API Management

**Status:** Accepted

**Decision.** Each bounded context exposes a GraphQL subgraph (Hot Chocolate). A Fusion gateway composes them into
one graph; entity resolution uses lookup fields (`clientById`). Composition runs in CI and fails on conflicts,
which acts as the cross-team contract test. APIM is the only public entry point: Entra ID token validation,
per-agency rate limiting and GraphQL depth/size limits. The gateway forwards the caller's token; subgraphs
re-authorize every field (PHI fields require `phi.read` and are access-audited).

**Alternatives.** Apollo Federation (adds a non-.NET router), REST + BFF per UI (more endpoints, more chattiness),
APIM synthetic GraphQL only (no entity resolution across subgraphs).

**Consequences.** Subgraph changes must be backward compatible (expand, migrate, contract) because subgraphs deploy
before the gateway. Evaluate Fusion v2 (composite schema specification) when it is generally available.
