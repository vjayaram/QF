# ADR 0005: Database schema as SQL Database Projects (DACPAC), not EF migrations

**Status:** Accepted

**Decision.** Tables are generated from the domain model into SQL Database Projects (Microsoft.Build.Sql). CI builds
DACPACs; CD publishes them with sqlpackage using an Entra token, blocking on possible data loss. EF Core maps to the
schema but never creates it.

**Why.** The schema is declarative and reviewed as SQL, DBAs can own it, security objects (RLS policies, grants)
live beside tables, and the same model that generates EF mappings generates DDL, so the two cannot drift.
Integration tests apply the same scripts to a SQL Server container.
