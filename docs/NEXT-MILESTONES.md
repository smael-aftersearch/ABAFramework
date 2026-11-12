# Next implementation milestones (2026-11-13)

The authoritative, maintained **54-feature status table** and prioritized open-task backlog live at [docs/IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md); the machine-readable source is [docs/implementation-status.json](implementation-status.json). This file is a high-level release narrative, not an independent status source. Run `python3 scripts/status_matrix.py --write` after every change.

## New P0 SQL isolation and release-integrity work (CI verified)

- **SQL security:** PostgreSQL FORCE RLS and SQL Server FILTER/BLOCK
  policy builders are in the generic Platform, with trusted per-transaction
  tenant scopes and actual raw-SQL/EF bulk mutation negative tests.
  Context-owned migrations, user membership and least-privileged production
  roles remain release blockers.
- **Supply chain:** the root framework and generated CI build and Trivy-scan
  the final runtime image (fixable CRITICAL severity); the root CI also
  creates/verifies signed SLSA provenance for its tested NuGet template
  only after all upstream verification succeeds. Immutable base image
  digests, publication signatures, HIGH-severity policy and production
  deployment controls remain open.

## Main product is the generic API starter

- **Now implemented and GitHub CI verified:** `dotnet new aba-api` template; generated OpenAPI, Scalar interactive API UI in Development, standard errors, correlation IDs, rate limits, OIDC/JWT fail-closed configuration, xUnit/WebApplicationFactory API tests, generic project instructions and AI docs.
- **Do not add Academy example business contexts** to new generated projects. The existing Academy Phase 1 sample remains optional and separate.

## Currently implemented and verified in the domain-neutral template

- **EF Core 10 both PostgreSQL and Microsoft SQL Server:** runtime provider selection; technical idempotency model with provider-neutral migration; real integration tests against both engines.
- **Redis:** optional distributed caching, scoped JSON keys, TTL and invalidation; strict fail-closed by default. Explicit read-through of *non-authoritative* query data on Redis network failure is tested with fault injection and the real Redis client against an unreachable TCP endpoint. Multi-node invalidation and reconnect/load resilience remain incomplete.
- **Wolverine 6:** durable EF transaction+outbox happy path and injected pre-commit rollback verified on both SQL engines, plus persistent scheduled-message recovery after a clean host restart. Real business handlers and arbitrary process-kill recovery remain unverified.
- **OIDC/JWT:** real ephemeral Keycloak OIDC discovery/JWKS and signed bearer 204/401/403 tests; Production requires HTTPS identity metadata, with HTTP loopback opt-in allowed only in Development. Claim-based tenant gates and EF row query/write guards are tested against both SQL engines. Full IdP membership, production RLS policy installation/role governance and operational key rotation are not delivered; generic RLS providers and raw/bulk negative checks are now technically verified.
- **OpenTelemetry:** optional ASP.NET+HttpClient tracing/metrics, validated OTLP endpoint, no collector auto-installed.
- **Status tracking:** the 54-capability JSON/README matrix and generated P0/P1/P2 backlog now pass six negative/positive checks plus a CI drift gate.
- **Quality and AI:** xUnit/WebApplicationFactory, provider/tenant/Outbox recovery CI, concurrent four-caller idempotency tests on both SQL engines, JWT/tenant-partitioned rate limits with health bypass, agent Skill/AGENTS and a portable `aba_task.py` document/evidence runner, installable NuGet template and optional per-context dual-provider `--with-db` EF scaffolding. Generated projects include their own default and manually triggered integration CI workflows.

[Source-grounded full framework CI evidence](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43298754432) | `docs/verification/2026-11-12-platform-providers.md`.

## Recent P0 infrastructure work — verified against disposable CI services

- **Recovery:** [CI run 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455)
  passed 7 provider-native backup/restore/safety tests on uniquely named
  disposable PostgreSQL and SQL Server databases. They require an explicit
  CI-only opt-in and prove data recovery after an injected mutation. Production
  restore/rollback rehearsal, business tables, backups and RTO/RPO are still open.
- **Dependency security:** the same CI passed 16 regression tests and independently
  scanned 161 default-Solution and 104 integration-project NuGet packages,
  generating separate SPDX 2.3 inventories. OCI scanning, digest
  pinning and artifact attestations remain open.

## Still incomplete (release blockers, not yet delivered)

1. **Wolverine release reliability:** technical outbox atomicity, injected pre-commit rollback and clean restart recovery are VERIFIED on both providers; still needed: actual context-owned domain handlers, ungraceful process-kill crash windows, poison/dead-letter/replay tooling, duplicate/concurrent consumers and optional broker transports.
2. **Production security:** Keycloak real OIDC/JWKS positive/negative token tests and EF query/SaveChanges tenant guards on both SQL providers are VERIFIED. Still needed: organization membership and RBAC/ABAC policies, approved production tenant RLS migrations and strict DB role grants, SQL-injection-safe raw/Bulk SQL boundaries, externally managed IdP key rotation and secure HTTPS deployment.
3. **EF Core production operations:** optional per-context empty DbContext scaffolding and a production-startup TLS verification guard now exist and passed direct + real Docker negative tests in [CI 43305238518](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43305238518). Real context-owned models/migrations, optimistic concurrency, forward/rollback/backup/restore drills and actual production certificate-chain validation remain to deliver with business ownership.
4. **Redis resilience:** an opt-in authoritative-source fallback for noncritical reads and real-client network-failure test now exist; remaining: controlled live restart/reconnect, distributed invalidation, tenant authorization across services, key rotation, stampede protection and load testing.
5. **OpenTelemetry and operations:** real collector/export verification, logs correlation, sampling/PII policy, health dependency readiness, performance baselines, load/failover tests, audit events and dashboards.
6. **Distribution/deployment:** the `0.3.0-preview.2` NuGet API template package and a non-root OCI image are build/test artifacts. Each generated project has default self-contained CI and an opt-in real-infrastructure workflow. Public NuGet.org publishing, signed attestations, selected cloud deployment, SLA/release automation, threat modeling and vulnerability scans remain incomplete.

## Supply-chain work (partial, P0)

- **Verified:** fail-closed direct/transitive NuGet audit and SPDX 2.3
  resolved-package inventory for the default generic API solution in both
  repository and generated-project CI; [run 43311281662](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43311281662)
  passed 13 negative/positive tests and produced a 161-package NuGet inventory.
- The isolated integration-test project is now scanned separately by CI.
  Mandatory next gates: scan and pin container image digests,
  and implement verifiable release signing/attestations and publication policy.

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

**Latest security evidence:** [all eight CI jobs passed](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) with 81 default API xUnit cases, 18 real-service SQL/Redis/Wolverine tests (including PostgreSQL/SQL Server RLS negative cases), 7 native recovery cases, runtime Trivy scan and signed template attestation with tamper rejection. Business membership and released-image signature controls remain unapproved.

## Opt-in audit integrity and Wolverine dead-letter operations

The generic API includes an opt-in HMAC-SHA256 audit ledger for PostgreSQL
and SQL Server (serializable transactional append, opaque actor/action IDs,
per-tenant sequence and chained digests, key rotation and verification).
Real-provider CI verifies chain integrity, tamper rejection and concurrent
appends. Customer business event selection, IdP membership, immutable
external digest checkpoints, HMAC key custody, PII retention policy,
least-privileged DB roles and deployment are **not** implemented.

The generic Wolverine operator is opt-in and **service-only**: it requires
durable messaging, the signed audit ledger, an authenticated operator
with a precise permission claim and a change ticket. Only one previously
inspected envelope with matching message/exception types can be marked
for replay; the message body stays private. Tenant mode is disabled
pending tenant-specific store authorization. Replay does not guarantee
a business outcome or prevent duplicate external side effects.
The synthetic poison-message SQL integration test is being verified
in GitHub Actions. See the template runbook and ADRs 006/007.
