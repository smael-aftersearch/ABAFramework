# ADR-002: Use a local file-based transactional message demonstration in Phase 1

**Status:** Temporary; must be superseded before production

**Context:** Requested Wolverine + PostgreSQL + Redis are not available in the current authoring environment, which has no .NET SDK/Docker and cannot resolve external package hosts. We need honest, source-based progress without fabricating successful integration.

**Decision:** A reusable technical adapter writes one context's state, inbox and outbox in a single atomic JSON-file replacement. An in-process dispatcher performs at-least-once delivery, bounded retries and dead-letter marking. Contexts consume versioned public events and suppress duplicates via inbox and business invariants.

**Alternatives:** Write speculative Wolverine integration without verifying APIs (unacceptable false confidence); no executable vertical slice (less useful); use many stubs (does not demonstrate ABA).

**Tradeoff:** Only safe as a single-process development demo; no production durability guarantees, broker, Redis, relational DB, cloud/tenant isolation or provider reliability.

**Consequences:** Introduce PostgreSQL/EF Core + verified Wolverine integration and real multi-host integration tests in the next milestone, preserve event contracts, then retire this adapter.
