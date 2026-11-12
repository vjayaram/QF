# ADR 0001: Decompose by bounded context, migrate with the strangler fig

**Status:** Accepted

**Context.** Several acquired EHRs (CareLogic, Credible, InSync) each own the same concepts (client, appointment,
note, claim) with different models. A big-bang rewrite or a lift-and-shift of each monolith would keep the
duplication and the risk.

**Decision.** Decompose by business capability into bounded contexts (Client Registry, Scheduling, then Billing &
Claims and Clinical Documentation). Start with Client Registry, the enterprise master client index, because every
other context references a client. Legacy systems keep running; change data capture and HL7 feeds flow through an
anti-corruption layer into the new contexts, and UI features move to the GraphQL graph one capability at a time.

**Consequences.** Each context has its own database and deploys independently. Cross-context consistency is
eventual (integration events). Legacy identities are kept on the client for traceability and back-references.
Probable duplicates go to a human review queue rather than being merged automatically.
