# Next implementation milestones (2026-11-12)

## Main product is the generic API starter

- **Now implemented and GitHub CI verified:** `dotnet new aba-api` template; generated OpenAPI, Scalar interactive API UI in Development, standard errors, correlation IDs, rate limits, OIDC/JWT fail-closed configuration, xUnit/WebApplicationFactory API tests, generic project instructions and AI docs.
- **Do not add Academy example business contexts** to new generated projects. The existing Academy Phase 1 sample remains optional and separate.

## Currently implemented and verified in the domain-neutral template

- **EF Core 10 both PostgreSQL and Microsoft SQL Server:** runtime provider selection; technical idempotency model with provider-neutral migration; real integration tests against both engines.
- **Redis:** optional distributed caching, scoped JSON keys, explicit TTL and removal, and actual Redis test.
- **Wolverine 6:** optional relational message persistence on either engine, durable local message processing, EF Core transactional middleware and real message handler probe in CI. This demonstrates baseline delivery only; full outbox/failure correctness requires more tests.
- **OIDC/JWT:** production startup refuses missing HTTPS authority/audience, route authorization defaults to required, 401 negative test. Optional `tenant_id` claim gate is newly implemented, **not** a substitute for complete tenant authorization/storage isolation.
- **OpenTelemetry:** optional ASP.NET+HttpClient tracing/metrics, validated OTLP endpoint, no collector auto-installed.
- **Quality and AI:** xUnit/WebApplicationFactory, real service CI, ABA governance, generic Skills/AGENTS metadata and .NET template instantiation.

[Source-grounded infrastructure CI evidence](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904) | `docs/verification/2026-11-12-platform-providers.md`.

## Still incomplete (release blockers, not yet delivered)

1. **Wolverine reliability:** DB transaction + outbound message in the same committed unit of work across real bounded-context handlers, injected crashes/restarts, idempotency on duplicate callbacks, dead-letter/replay tooling, poison messages and broker-backed delivery when needed.
2. **End-to-end security:** Keycloak/IdP with valid and invalid signed tokens, audience/issuer/key rotation, 401/403, per-tenant ownership and EF Core **row isolation** tests. Optional claim checking is only a baseline.
3. **EF Core production operations:** per-business-context models/migrations, concurrency/race and forward/rollback tests, encrypted SQL Server TLS configuration, PostgreSQL operational backups, migration orchestration and safe rollback.
4. **Redis resilience:** outage fallback, distributed invalidation, cross-service tenancy checks, key rotation, race/cache stampede behavior and load tests.
5. **OpenTelemetry and operations:** real collector/export verification, logs correlation, sampling/PII policy, health dependency readiness, performance baselines, load/failover tests, audit events and dashboards.
6. **Distribution/deployment:** reusable versioned NuGet packages, published template packages, real container deployment and release pipeline after threat modeling, supply-chain and vulnerability checks.

## Release gates

Never confuse a successful generated API starter or Academy smoke run with production readiness. Each implemented adapter must have focused unit tests, failure tests, integration tests and evidence. Keep this status updated to match actual files and CI results.

---

## Previous Phase 1 planning notes

# Work remaining to reach the requested full framework

The original requirement is in `docs/reference/TARGET-SPECIFICATION.md`. The phase-1 source demonstrates an initial ABA vertical slice and is intentionally incomplete.

## Milestone 2 — verified build + PostgreSQL / EF Core 10

1. Execute actual .NET 10 restore/build and the console smoke suite; resolve any compiler/runtime defects.
2. Introduce one PostgreSQL schema and context-owned `DbContext` per business context, complete EF Core migrations, explicit concurrency handling, and SQL transaction tests.
3. Replace JSON-file atomics with a real relational transaction writing business state and the Outbox together. Preserve `IOutboxSource` semantics or adapt interfaces with an ADR.
4. Test database retries, crashes between commit/publication, simultaneous requests, and cold-start migration behavior with Testcontainers.

**Gate:** a clean PostgreSQL instance supports the same API flow and restart recovery without local JSON state files.

## Milestone 3 — Wolverine, Redis and observability

1. Verify current WolverineFx stable APIs and .NET 10/EF Core integration against official documentation.
2. Use Wolverine durable Inbox/Outbox (and compatible broker transport where justified), retry/error queues, correlation IDs, and consumer-level idempotency.
3. Replace local `IReadCache` implementation with Redis-backed or hybrid caching; define cache invalidation and tenant-safe keys.
4. Add OpenTelemetry traces/metrics/log correlation, health probes and structured operational dashboards.

**Gate:** loss/retry/duplicate tests pass across independently restarted host and infrastructure containers.

## Milestone 4 — secure, production-aware API

1. Add Keycloak or another standards-based OIDC identity provider, OAuth 2.0, bearer-token validation and policy authorization.
2. Define tenant boundaries and isolate all owned storage and cache keys.
3. Add ASP.NET-generated OpenAPI, API compatibility testing, validation/ProblemDetails, throttling, structured audit logs, and secure configuration.
4. Require authentication/authorization end-to-end and remove the development-only production guard only after explicit security review.

**Gate:** documented threat-model checks, invalid-token tests and tenant-isolation tests pass.

## Milestone 5 — packaging and developer workflow

1. Publish reusable framework NuGet packages and at least one `dotnet new` template.
2. Expand governance with contract compatibility against a baseline, IL-level dependency checks/analyzers, API drift detection, and automatic dependency graph output.
3. Add professional xUnit projects, CI restore/build/test/vulnerability scanning, template instantiation tests, and migration guide.
4. Extend sample to realistic provider callback validation, refunds/reconciliation, append-only correction journals, notification adapters and compensation policies (only when justified by ABA).

**Gate:** an engineer can generate and build a new application, add a bounded context, and deploy locally by following the README only.

## Agent/documentation automation hardening (future)

The first document-driven ABA Skill has been added and verified with Python tests. Further useful work is to (1) compare changed contracts against a baseline and detect breaking API/event changes, (2) validate source-to-document references and freshness on PR diffs, (3) test the actual skill against multiple real coding tasks and score false negatives, and (4) optionally surface documentation/ownership health in a dashboard. These are **not yet implemented**. Avoid pretending a static document map can prove semantic correctness.
