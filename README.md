# ABA Framework — .NET 10 generic API quick start

**Verified technical baseline (2026-11-13):** [all eight GitHub Actions jobs passed](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) on commit `dd77a1c1b0f2`. A fresh generic API passed **99 API xUnit tests**, **26 real PostgreSQL/SQL Server/Redis/Wolverine integration tests**, **7 provider-native recovery/safety cases** and **8 real SIGKILL crash drills** covering pre-commit rollback, persisted schedule recovery and pre-consumer-ACK redelivery with one idempotent technical effect. Separately signed audit checkpoint tests, Keycloak OIDC/JWKS, NuGet vulnerability/SPDX audits, Trivy runtime image scanning and signed Sigstore/SLSA NuGet provenance also passed. **This does not certify real business Outbox atomicity, external side-effect exactly-once, immutable Production audit custody or deployment readiness.**


**Start here if you want a new, ordinary API.** The domain-neutral `aba-api` template is the primary deliverable. You do **not** need the Academy enrollment/payment example to create your product.

```bash
git clone https://github.com/smael-aftersearch/ABAFramework.git
cd ABAFramework
dotnet new install ./templates/aba-api
dotnet new aba-api -n MyCompany.MyApi -o ../MyCompany.MyApi
cd ../MyCompany.MyApi
dotnet run --project src/MyCompany.MyApi.Api
```

Use .NET 10 SDK. In Development open `http://127.0.0.1:5174/scalar` for the **interactive API reference**, `/openapi/v1.json` for generated OpenAPI, or `/health/live` for liveness and `/health/ready` for dependency readiness. Run `dotnet test MyCompany.MyApi.slnx` for **real xUnit tests** included in every generated project.

The generated project has no Academy or business examples. It includes a versioned HTTP route, OIDC/JWT startup guard (non-Development requires HTTPS issuer and audience), protected endpoints by default, standard Problem Details, correlation IDs, rate limiting, test cases, architecture documentation and AI agent instructions. See `templates/aba-api/README.md` and `docs/architecture.md`.

**CI verification:** `bash scripts/verify-starter.sh` installs the template, generates a fresh named solution, runs restore/build/xUnit and checks the real local HTTP host. The original Academy sample remains an **optional reference only**, not the base API.

## Ten-run hourly delivery tracker (started 2026-11-13)

The [hourly delivery control issue](https://github.com/smael-aftersearch/ABAFramework/issues/6) tracks **23 remaining generic-template capabilities** from [the authoritative status catalog](docs/implementation-status.json). The initial reviewed baseline is **31 implemented, 10 partial, 13 not started** out of 54. A partial feature is **not** counted as completed. Each queue has a maximum of ten executions (one immediate run and nine scheduled hourly runs); missed or blocked production gates are reported, not auto-declared complete.

| Queue / owners | Capabilities still open at baseline | Partial | Not started | Newly completed and verified this batch | Tracking |
| --- | ---: | ---: | ---: | ---: | --- |
| A — release/CI, audit, telemetry, operations | 7 | 3 | 4 | 0 / 7 | [#3](https://github.com/smael-aftersearch/ABAFramework/issues/3) |
| B — persistence, messaging, caching | 9 | 4 | 5 | 0 / 9 | [#4](https://github.com/smael-aftersearch/ABAFramework/issues/4) |
| C — security, API and business contracts | 7 | 3 | 4 | 0 / 7 | [#5](https://github.com/smael-aftersearch/ABAFramework/issues/5) |
| **Total** | **23** | **10** | **13** | **0 / 23** | [Ledger](https://github.com/smael-aftersearch/ABAFramework/issues/6) |

The table records *verified feature closures*, not speculative percentage-complete estimates. For exact feature IDs, acceptance criteria, owners and evidence, follow [the generated backlog](docs/IMPLEMENTATION-STATUS.md). After each substantive code change, update the README and source status JSON only to reflect actual tested changes, regenerate the status matrix, and record the PR/CI evidence in the relevant issue. Automated merge is allowed only after real tests, review and branch-policy gates. Business-owner approvals, production secrets, release policy, and deployment decisions remain outside the authority of these tasks.

## Supply-chain audit and SBOM

The latest eight-job GitHub CI verifies the fail-closed NuGet direct/transitive
advisory gate with **16 positive/negative tests**. Two separate SPDX 2.3
inventories cover **161 resolved packages in the default API solution** and
**104 packages in the external integration-test project**. Neither scan
reported a known advisory in that run. OCI image scanning/digest pinning,
signed artifacts and publication provenance remain unverified. See
[template instructions](templates/aba-api/README.md).

## CI-backed database recovery and complete NuGet coverage

The generic starter now includes opt-in, **CI-only native backup/restore drills**
for disposable PostgreSQL and SQL Server databases. The drills passed in
[CI run 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455).
They backup a marker,
mutate the source, restore, and require the original marker to return. The test
code rejects external databases or missing CI opt-in, and ordinary developer
test runs exclude destructive cases. See
[the recovery playbook](templates/aba-api/docs/disaster-recovery.md).
This is **not** a production backup, business schema migration rollback, PITR,
RTO/RPO or encrypted offsite recovery certification.

The NuGet advisory/SBOM gate now audits both the fast default API solution
**and the separate integration-test project** rather than omitting the latter.
OCI image scanning/pinning and signed provenance remain open P0 release tasks.
The maintained capability matrix below distinguishes these partial steps from
production readiness.

## Database-enforced tenant isolation and raw SQL protection

The generic Platform now contains **opt-in PostgreSQL and SQL Server RLS
policy builders** and a trusted `AbaTenantSqlScope.RunAsync` transaction
boundary. The new real-provider tests check cross-tenant raw SQL, forged
inserts and EF `ExecuteUpdate`/`ExecuteDelete` that bypass the ordinary
EF query filter. Business owners still need to configure actual membership
permissions, schema migrations, separate low-privilege runtime DB roles and
SQL injection prevention. See the
[RLS design decision](templates/aba-api/docs/decisions/ADR-005-tenant-row-security.md).

## Container vulnerability and signed package provenance gates

A commit-pinned Trivy scanner now inspects the built runtime image and
rejects fixable CRITICAL advisories. The root main-branch CI also requires
the NuGet template package to pass signed Sigstore/SLSA build-provenance
verification, including a negative tamper check, **after** all other
verification jobs succeed. The generated project's CI includes its own
OCI scan. See [release integrity](docs/release-integrity.md) for what is
enforced and what remains unverified (including base-image digest pinning,
publication/signing of OCI images and Production secrets).


## Opt-in HMAC audit and controlled Wolverine dead-letter recovery

The Platform now provides a database-backed, tamper-evident HMAC audit
ledger with per-tenant digest chaining, transactional append, key rotation
and integrity verification. The feature is off by default and requires
externally managed signing keys. A separate signed head checkpoint is
required to detect deletion of a terminal suffix.

Wolverine dead-letter inspection and replay are exposed only through
an opt-in, permission-gated **service**, never an anonymous or public
HTTP endpoint. Operators need a valid identity, exact permission,
change ticket, exact envelope ID and expected message/exception types.
Replay requests are recorded to the HMAC ledger first. Multi-tenant
administration is deliberately disabled pending business authorization.

See the [audit and dead-letter runbook](templates/aba-api/docs/operations/audit-deadletters.md)
plus [audit ADR](templates/aba-api/docs/decisions/ADR-006-audit-ledger.md)
and [replay ADR](templates/aba-api/docs/decisions/ADR-007-wolverine-deadletter-operator.md).
Neither service decides business membership, retention or side-effect
compensation; these remain owner-defined release gates.

## SIGKILL durability drills and separately signed audit checkpoints

The source framework now includes explicit CI-only process-kill tests
on disposable PostgreSQL and SQL Server. They verify an uncommitted
EF write is rolled back and a persisted delayed Wolverine message
can be recovered after the original host is killed; full Outbox
publish/consumer ACK windows remain open. The new CrashDrill
category is excluded from ordinary tests and runs only with
explicit disposable-CI authorization.

The audit ledger now produces unsigned head candidates that a
separate trusted custodian can sign with an independent HMAC key.
The API verifies that signed historical prefix even after later
audit events are appended, but does not deploy an immutable
offsite checkpoint store or Production key manager. See
[crash ADR](templates/aba-api/docs/decisions/ADR-008-kill-window-recovery.md),
[checkpoint ADR](templates/aba-api/docs/decisions/ADR-009-offline-audit-checkpoints.md)
and [safe crash runbook](templates/aba-api/docs/operations/crash-drills.md).

## Host-wide cache-miss coordination and reviewed OCI supply chain

The generic API Platform now coalesces concurrent authorized
read-through misses for the SAME scoped key within one process;
different tenants/keys do not block one another. Default(T) and JSON
null cache hits are distinguished from misses. A separately provisioned
Redis CI job actually stops/restarts its disposable Redis service and
proves strict failure, explicit fresh-source fallback and reconnect.
These are NOT multi-replica distributed locks or authorization controls.

Generated containers now pin reviewed .NET 10 SDK and ASP.NET Core base
**manifest digests** via `oci-base-lock.json`; upstream and generated
CI reject floating/unapproved bases. The root main-branch build also
signs the **actual scanned Docker-save image archive** after verifying
its loaded image ID and rejecting a tampered archive. This is not
a registry-signed production OCI digest. Read
[cache ADR](templates/aba-api/docs/decisions/ADR-010-cache-singleflight-redis-restart.md),
[release ADR](templates/aba-api/docs/decisions/ADR-011-oci-base-and-archive-attestation.md)
and [release integrity policy](docs/release-integrity.md).

## Installable template package

CI also builds and tests a versioned `ABAFramework.GenericApi.Template.*.nupkg` NuGet template artifact. After downloading it from the successful [GitHub Actions](https://github.com/smael-aftersearch/ABAFramework/actions) run, install with `dotnet new install ./ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg`, then `dotnet new aba-api -n YourCompany.Api`. This is an artifact attached to CI, **not yet a NuGet.org publication**. Pack locally with `bash scripts/pack-template.sh`.

## Roadmap and scope

**Implemented technical integrations (opt-in):** EF Core 10 for **both PostgreSQL and Microsoft SQL Server**, provider-neutral migrations, relational idempotency receipts, Redis distributed cache with TTL and scope isolation, Wolverine 6 durable local messaging with EF transactional middleware for either SQL engine, and OpenTelemetry traces/metrics with optional OTLP export. Optional authenticated JWT tenant-claim enforcement and context-owned EF Core query/write guards are implemented; real tests on **both** database engines verify denial of cross-tenant EF reads and writes. This does **not** provide SQL-level RLS, trusted tenant membership or protection against raw SQL/bulk operations. All dependencies default to off so the blank API starts immediately.

**Current evidence:** [full generic-framework run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455) verifies 16 real-service scenarios (Wolverine transactional outbox and clean restart on both engines, transactional idempotency, EF tenant protection on both SQL engines, Redis), plus actual Keycloak/OIDC tests, Docker startup, xUnit and template packaging. The module generator creates Domain/Application/Infrastructure/Contracts, owner metadata and tests; `--with-db` adds an optional context-owned EF shell for either SQL engine. **Not certified:** ungraceful process-kill recovery, poison/dead-letter replay, live tenant membership and DB RLS, external broker, migration rollback/backup plans, load/security audit or production deployment.

Setup and connection settings: [generic platform infrastructure guide](templates/aba-api/docs/platform-infrastructure.md). Local optional services: `docker compose -f infra/compose.platform.yaml up -d`. Remaining quality gates: `docs/NEXT-MILESTONES.md`.

---

## Implemented versus not yet implemented

<!-- ABA-STATUS:START -->

The **generic .NET 10 API template** is the primary product. This matrix is separate from the
historical Academy Phase 1 demo below. A feature can be implemented technically while
its **production release gates remain open**. Evidence refers to the last reviewed baseline:
[CI run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289)
(commit `dd77a1c1b0f2`). New commits must pass their own CI before being treated as verified.

**Tracked: 54** · Implemented: 31 · Partial: 10 · Not implemented: 13.

| Area | Parameter / feature | Status | Delivered scope | Remaining task | Priority |
| --- | --- | --- | --- | --- | --- |
| API | Domain-neutral .NET 10 HTTP API | **Implemented** | Versioned Minimal API with composition root | No generic framework work outstanding | P0 |
| API | OpenAPI plus interactive Scalar | **Implemented** | Generated OpenAPI and Development-only Scalar UI | Production API docs need a reviewed access policy | P1 |
| API | Problem Details and HTTP errors | **Implemented** | RFC 7807 HTTP 500 with nonsecret Production response, validated header/body correlation ID and focused HTTP failure tests | Review validation/client errors, proxy traces and application logging policy per deployment | P1 |
| API | Request correlation IDs | **Implemented** | Validated X-Correlation-ID response header | Correlate logs, traces and message envelopes across services | P1 |
| API | Liveness and readiness | **Implemented** | Independent liveness and ready-tagged dependencies; injected health failure returns live 200 and ready 503 | Prove real database/Redis outage, reconnection and readiness behavior under load | P0 |
| API | Authenticated per-identity rate limits | **Implemented** | JWT subject + tenant partition; anonymous TCP peer; Health bypass | Multi-node shared quotas require a gateway | P1 |
| API | Distributed multi-instance rate limits | **Not implemented** | Not implemented | Add shared gateway/quota store with trusted proxy policy | P1 |
| Security | OIDC/JWT signature, issuer and audience | **Implemented** | ASP.NET JWT bearer validation and fail-closed production OIDC config | External real-user IdP configuration and security operations remain | P0 |
| Security | Real Keycloak OIDC/JWKS test | **Implemented** | Ephemeral Keycloak CI verifies signed 204, missing/malformed 401, tenant 403 | Real IdP key rotation and organization policy not validated | P0 |
| Security | Trusted tenant claim required when enabled | **Implemented** | JWT tenant claim fallback/default authorization; ignores raw tenant header | Prove organization membership in actual IdP | P0 |
| Security | Tenant membership / RBAC / ABAC | **Not implemented** | Not implemented | Define owning membership model, roles and permissions | P0 |
| Security | EF tenant row query and SaveChanges guards | **Implemented** | Tenant-scoped EF filtering and foreign-row write rejection | Raw SQL, bulk updates and SQL-level RLS remain open | P0 |
| Security | Database-enforced Row-Level Security | **Partial** | CI-verified PostgreSQL FORCE RLS and SQL Server FILTER/BLOCK DDL, trusted tenant transaction scopes, and real cross-tenant raw SQL + EF bulk negative tests on both databases | Apply owner-specific RLS migrations and least-privilege roles, verify membership authorization and privileged/session spoof bypass cases | P0 |
| Security | Bulk/raw SQL tenant authorization | **Partial** | CI-verified tenant-scoped SQL transaction wrapper; direct SQL and EF ExecuteUpdate/ExecuteDelete cross-tenant denial, forged insert rejection and session isolation on both SQL engines | Block SQL-injection tenant session spoofing, adopt real context-aware raw SQL authorization and least-privileged production accounts | P0 |
| Persistence | Production SQL TLS and certificate identity | **Implemented** | Production startup rejects PostgreSQL without VerifyFull and SQL Server without verified encryption; real container negative tests passed | Configure real certificate authority/trust chain and rotation in target deployment | P0 |
| Persistence | EF Core 10 PostgreSQL provider | **Implemented** | Runtime Npgsql provider selected by config | Context-owned schema migrations require real domains | P0 |
| Persistence | EF Core 10 Microsoft SQL Server provider | **Implemented** | Runtime EF SqlServer provider selected by config | Context-owned schema migrations require real domains | P0 |
| Persistence | EF technical idempotency migration | **Implemented** | Provider-neutral checked-in EF technical migration | Production migration rollback/backup remains unverified | P0 |
| Persistence | Optional per-context EF Core scaffold | **Implemented** | --with-db creates empty context-owned DbContext for both SQL engines | No business entities, actual migrations or policies are invented | P1 |
| Persistence | Business context schema and migrations | **Not implemented** | Not implemented | Implement real owner-specific models and migrations after requirements | P0 |
| Persistence | Business optimistic concurrency handling | **Not implemented** | Not implemented | Implement rowversion/concurrency tokens and conflict semantics per context | P1 |
| Persistence | Backup, recovery, rollback and restore drill | **Partial** | CI-verified disposable PostgreSQL 16 native dump/restore and SQL Server native checksum BACKUP/RESTORE: 7 safety/recovery tests; production backup/rollback not delivered | Prove business-schema and point-in-time recovery, migration rollback, offsite encrypted backups, RTO/RPO and operator restore rehearsal | P0 |
| Reliability | Transactional idempotency receipt and work | **Implemented** | Same-DbContext work and receipt commit/rollback together | External provider side effects still require dedicated idempotency | P0 |
| Reliability | Concurrent duplicate request suppression | **Implemented** | Four concurrent callers; only one same-key transaction commits | Cross-service external payment retries need real owning context | P0 |
| Messaging | Wolverine 6 durable local queues | **Implemented** | Durable relational messaging with either SQL engine | External broker delivery not yet wired | P0 |
| Messaging | EF Core/Wolverine technical transactional Outbox | **Implemented** | Technical state+outgoing message atomic under precommit fault | Real business consumers and crash windows remain open | P0 |
| Messaging | Durable scheduled message across clean restart | **Implemented** | Persisted scheduled Wolverine message recovers after host restart | Abrupt crash and poison replay remain unverified | P1 |
| Messaging | Poison/dead-letter inspection and replay | **Partial** | CI-verified opt-in Wolverine poison quarantine, metadata-only inspection and exact-ID audit-before-replay with permission/type/mismatch denial; 2 real SQL providers exercised | Issue real signed operator IdP permission, tenant-scoped message store authorization, replay dedup/business compensation, volume limits and approved production runbook | P0 |
| Messaging | Crash-kill Outbox recovery and duplicate delivery | **Partial** | CI-verified real SIGKILL on both PostgreSQL and SQL Server: uncommitted SQL rollback, durable Wolverine schedule recovery and genuine pre-ACK redelivery after one separately committed idempotent technical effect (8/8 CrashDrill cases) | Owner-specific transactional business Outbox COMMIT/publish/consumer ACK fault tests, external provider side-effect deduplication, broker partitions, multi-node fencing and Production recovery approvals | P0 |
| Messaging | Optional external RabbitMQ/Kafka transport | **Not implemented** | Not implemented | Add a versioned optional broker adapter when actual demand exists | P1 |
| Messaging | Real context-owned events and consumers | **Not implemented** | Not implemented | Implement owner-approved domain contracts, Inbox and cross-context handlers | P0 |
| Caching | Redis distributed cache with TTL and scopes | **Implemented** | StackExchangeRedis adapter with scoped JSON TTL/invalidation | Distributed invalidation and PII audit remain open | P1 |
| Caching | Read-through fallback when Redis unavailable | **Implemented** | Default fail-closed; explicit fresh authoritative query fallback | Cluster reconnect, authorized workloads and load behavior unverified | P1 |
| Caching | Cross-instance invalidation and tenant permissions | **Not implemented** | Not implemented | Add versioned invalidation events with authorized namespace boundaries | P1 |
| Caching | Cache stampede and Redis restart resilience | **Partial** | Host-wide per-tenant/key single-flight read-through with 24-concurrent-call and real 12-caller Redis tests, TTL/cancellation/miss-value fixes; actual CI Redis stop/restart with strict/explicit fallback and reconnect (isolated 2-case drill) | Add distributed multi-replica miss budget/locking, authorize query scopes, bound source load during full Redis outage and verify production Redis topology/TTL race policies | P1 |
| Observability | OpenTelemetry ASP.NET/HTTP traces and metrics | **Partial** | Optional tracing/metrics instrumentation and OTLP exporter wiring | Prove export to a real collector and enforce PII/sampling policy | P1 |
| Observability | Collector pipeline and export integration test | **Not implemented** | Not implemented | Add isolated OTLP receiver/collector to CI and verify trace/metric export | P1 |
| Observability | Tamper-evident audit trail | **Partial** | CI-verified HMAC-SHA256 per-tenant ledger, serializable dual SQL append, tamper/rotation tests plus separately signed offline checkpoint codec and historical-prefix verification on both SQL engines | Deploy independently immutable off-DB checkpoint archive, real signing key custody/rotation and context-owned actor/event/retention rules; audit appender role restrictions and legal policies | P0 |
| Operations | Production secrets, key rotation and policy | **Not implemented** | Not implemented | Add approved secret store, rotation and least-privilege deployment setup | P0 |
| Developer | Installable versioned NuGet API template | **Implemented** | Installable versioned v0.3.0-preview.2 NuGet API template built, installed and tested with 73 xUnit cases in CI | NuGet.org publication, provenance/signing and release automation remain gated | P1 |
| Developer | Domain/Application/Contracts/Infrastructure generator | **Implemented** | Scaffold real named contexts with ownership docs; no fictional entities | Complete real domain behavior remains owner-dependent | P1 |
| Governance | ABA ownership and forbidden-edge checks | **Implemented** | Repo and generated-project architecture checks with negative cases | Semantic/IL enforcement still requires human architecture review | P0 |
| Governance | Portable agent Skill, task plan and verification | **Implemented** | AGENTS, Skill, task-aware reader, PASS/FAIL/NOT_RUN evidence | AI agent adoption varies by platform; semantic review still manual | P1 |
| Governance | Machine-readable feature status and remaining tasks | **Implemented** | 54-feature machine-readable catalog drives README and detailed backlog; six negative/positive tests and CI drift gate passed | Maintain each feature's owner, test evidence, acceptance gate and backlog after future tasks | P1 |
| Testing | Default xUnit HTTP/authorization/EF tests | **Implemented** | 60+ API xUnit checks in fresh generated solution | Business invariants and load testing need real domains | P0 |
| Testing | Real PostgreSQL/SQL Server/Redis/Wolverine CI | **Implemented** | 16 executable real-service tests across both SQL engines | Crash, high-load and external broker remain release gates | P0 |
| Testing | CI bundled into each generated API | **Implemented** | Push/PR build gate and manually triggered real-service workflow | Generated-project pipelines need repository policy/hardening to be adopted | P1 |
| Deployment | Non-root OCI image and local Compose | **Implemented** | Multi-stage .NET 10 Dockerfile, localhost-only Development Compose | Production image signing, secrets/TLS/hosting still open | P1 |
| Deployment | SBOM, vulnerability scans and signed releases | **Partial** | NuGet direct/transitive advisory scans + SPDX; reviewed immutable .NET 10 multiarch SDK/runtime base digests with fail-closed policy/negative tests; Trivy-scanned runtime Docker-save archive keyless SLSA attested, Docker reload ID/tamper rejection; signed tested NuGet template provenance | Approve HIGH/unfixed image severity policy, sign and verify the actual published OCI registry digest and published NuGet package, protected tag promotion and production deployment/change-control | P0 |
| Deployment | Production deployment with TLS, identity and rollback | **Not implemented** | Not implemented | Choose target hosting, configure DNS/TLS/secrets and deployment rollback | P0 |
| Operations | Latency/throughput/load and failover baseline | **Not implemented** | Not implemented | Set SLO and run load/chaos tests across two provider deployments | P1 |
| Business | Owner-defined versioned cross-context contracts | **Partial** | Context generator creates Contracts project; no real product Contracts | Specify real public V1 schemas, consumers and compatibility evolution | P0 |
| Business | Consumer-driven contract compatibility tests | **Not implemented** | Not implemented | Introduce consumer-driven tests for real event/HTTP contracts | P0 |
| Business | Actual product CRUD/workflows and invariants | **Not implemented** | Not implemented | Implement real domain entities/endpoints only from approved requirements | P0 |

### Release-critical tasks still open (P0)

| Task ID | Task / acceptance gate | Current state |
| --- | --- | --- |
| `auth-membership` | Define owning membership model, roles and permissions — Unauthorized member and cross-organization privilege escalation tests | Not implemented |
| `auth-sql-rls` | Apply owner-specific RLS migrations and least-privilege roles, verify membership authorization and privileged/session spoof bypass cases — Direct SQL and EF ExecuteUpdate cannot modify foreign tenant records | Partial |
| `auth-raw-sql` | Block SQL-injection tenant session spoofing, adopt real context-aware raw SQL authorization and least-privileged production accounts — Cross-tenant raw SQL and bulk mutation are denied | Partial |
| `db-business-migrations` | Implement real owner-specific models and migrations after requirements — Both SQL engines perform context migration without shared business tables | Not implemented |
| `db-backup-restore` | Prove business-schema and point-in-time recovery, migration rollback, offsite encrypted backups, RTO/RPO and operator restore rehearsal — Recover both SQL engines and confirm data integrity after test failure | Partial |
| `msg-poison-replay` | Issue real signed operator IdP permission, tenant-scoped message store authorization, replay dedup/business compensation, volume limits and approved production runbook — Poison message is quarantined, inspected and safely replayed exactly as intended | Partial |
| `msg-crash-window` | Owner-specific transactional business Outbox COMMIT/publish/consumer ACK fault tests, external provider side-effect deduplication, broker partitions, multi-node fencing and Production recovery approvals — No message loss across abrupt process death and controlled duplicate deliveries | Partial |
| `msg-domain-consumers` | Implement owner-approved domain contracts, Inbox and cross-context handlers — Consumer cannot mutate another context's tables; duplicate events are safe | Not implemented |
| `ops-audit` | Deploy independently immutable off-DB checkpoint archive, real signing key custody/rotation and context-owned actor/event/retention rules; audit appender role restrictions and legal policies — Audit cannot be silently rewritten and records actor, action, tenant, outcome | Partial |
| `ops-prod-secrets` | Add approved secret store, rotation and least-privilege deployment setup — No plaintext production secret in repo/container/CI logs | Not implemented |
| `dev-release-sbom` | Approve HIGH/unfixed image severity policy, sign and verify the actual published OCI registry digest and published NuGet package, protected tag promotion and production deployment/change-control — Release pipeline rejects vulnerable/untrusted dependencies and verifies provenance | Partial |
| `dev-prod-deploy` | Choose target hosting, configure DNS/TLS/secrets and deployment rollback — Production smoke, zero-downtime deploy and rollback drill pass | Not implemented |
| `biz-contracts` | Specify real public V1 schemas, consumers and compatibility evolution — V1 consumers stay compatible; private implementation edges rejected | Partial |
| `biz-contract-tests` | Introduce consumer-driven tests for real event/HTTP contracts — A breaking V1 change fails CI and requires an explicit V2 | Not implemented |
| `biz-endpoints` | Implement real domain entities/endpoints only from approved requirements — Owner-specific happy/negative/invariant and provider integration tests pass | Not implemented |

For exact code paths, tests and the P0/P1/P2 backlog, read
[docs/IMPLEMENTATION-STATUS.md](docs/IMPLEMENTATION-STATUS.md).
Statuses are **maintainer reviewed**, not inferred by the mere existence of a file.
Run `python3 scripts/status_matrix.py --check` to fail CI on mismatched tables.

<!-- ABA-STATUS:END -->

## Optional legacy example: Academy (Phase 1 Developer Preview)

The following section documents the old sample app and its limitations; it is **not part of projects created with `aba-api`**.

# ABA .NET 10 Framework & Academy — Phase 1 (Developer Preview)

A **real source-code starting point** for an ABA-aligned C#/.NET 10 modular monolith, implementing a complete **paid enrollment vertical slice** across five bounded contexts. This is a **development-only preview** with intentionally limited infrastructure, not a finished production framework.

**Status (2026-11-12):** The Phase-1 starter is committed on `main`. [GitHub Actions CI](https://github.com/smael-aftersearch/ABAFramework/actions) has verified .NET 10 restore, Release build, Academy console smoke, and architecture/AI governance. The original authoring environment lacked the .NET SDK; CI is the evidence for runtime compilation. **This is a local-development demo, not a public production deployment.**

### Historical Academy Phase 1 matrix (sample only — not the generic API)

| Area | Phase 1 status |
| --- | --- |
| C# and .NET 10 project structure | 15 source/test projects in a `.slnx` solution |
| Five ABA business contexts | Identity, Enrollment, Payments, Accounting, Reporting |
| Domain invariants / ownership | Implemented in context-owned source code |
| Versioned cross-context Contracts | Implemented; context implementations reference only other Contracts |
| Transactional local Outbox / Inbox | Implemented using atomic file replacement, **single process only** |
| At-least-once delivery / retry / dead letter | Implemented local dispatcher; no external broker |
| Simulated payment, idempotency, balanced journals | Implemented for local workflow |
| Caching | Local in-memory TTL cache + invalidation only; **NOT Redis** |
| HTTP endpoints / OpenAPI | ASP.NET Minimal API + static OpenAPI document (not generated) |
| Executable architecture governance | Implemented with negative tests |
| PostgreSQL / EF Core 10 | **NOT implemented** |
| Wolverine / RabbitMQ | **NOT implemented** |
| Redis distributed cache | **NOT implemented** |
| OAuth/OIDC, JWT, tenant isolation | **NOT implemented**; host deliberately refuses non-development mode |
| NuGet packaging / `dotnet new` | **NOT implemented** |
| OpenTelemetry, integration testcontainers | **NOT implemented** |

For the intended end-state see `docs/reference/TARGET-SPECIFICATION.md` and `docs/NEXT-MILESTONES.md`.

## AI-first, document-driven development (new)

This repository now includes the reusable **ABA Engineering Skill** at `.agents/skills/aba-engineering/SKILL.md` and a concise project-wide `AGENTS.md`. See `docs/ai/INDEX.md` for the source-of-truth map and `docs/ai/CHANGE-PROTOCOL.md` for the discovery-to-verification loop. The skill is also distributed separately as `skill.zip` for installation where Skills are supported; repository-local guidance stays with the source code.

For new tasks, run `python3 scripts/ai_check.py brief --task "add a payment event"` to list relevant documents and `python3 scripts/task_protocol.py plan --task "add a payment event"` to propose a scope-aware **test plan**, then **confirm the owner manually against context metadata**. `python3 scripts/ai_check.py check` and `python3 scripts/test_ai_check.py` validate the document discovery contract and fail on intentionally broken agent instructions. GitHub Copilot, Claude Code, and Gemini-style repo pointers are included; actual auto-loading depends on each agent's supported conventions and settings. Instructions cannot guarantee compliance by themselves—ABA governance/CI is the enforcement layer.

The skill makes the coding agent responsible for updating applicable metadata, catalog entries, ADRs, API docs and verification reports alongside code. It does **not** autonomously run in the background or claim that a planned framework capability is implemented.

## Prerequisites

- .NET 10 SDK (10.0.100 or compatible feature band). See `global.json`.
- Python 3.10+ (standard library only) for governance.
- No NuGet packages, PostgreSQL, Redis, or external services needed **for this first iteration**.

## Testing structure and agent responsibilities

**Existing tests** are at `tests/Aba.SmokeTests/Program.cs` (C# console runner) and `scripts/test_governance.py` / `scripts/test_ai_check.py` (Python negative rule tests). The complete inventory, test types per feature, proposed xUnit/Integration/Architecture/E2E directories and acceptance policy are in **`docs/testing/TEST-STRATEGY.md`** and `tests/README.md`. Those additional .NET test suites are future work, not current accomplishments.

Run `python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json` to record actual Python check outcomes. `--profile all` additionally needs .NET 10 and runs restore/build/console smoke; it records `NOT_RUN` if unavailable. CI uploads JSON/Markdown evidence. The planner and runner have negative regression tests in `scripts/test_task_protocol.py`.

For every material task, the ABA Engineering Skill requires an acceptance test plan (positive, negative, invariant), test code alongside source changes, actual execution when tools are available, and a truthful PASS/FAIL/NOT RUN report. `dotnet test` is not the runner for the current console smoke project.

## Verify from a fresh checkout

You can also run the single entrypoint `bash scripts/verify-all.sh`.

```bash
python3 scripts/task_protocol.py plan --task "Add payment refund" --file samples/Academy/Payments.Contracts/PaymentContracts.cs
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json
# Full gate requires .NET 10 SDK:
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json
```

The smoke suite is a dependency-free console program, not an xUnit/NUnit project. It covers the real cross-context flow, local restart persistence, event redelivery/idempotency, full capacity/waitlist behavior, and accounting invariants. Its C# executable smoke suite is verified by the successful GitHub Actions run; it was not executed in the original authoring environment.

## Start the development API

Clone the repository and install the .NET 10 SDK, then **run one command** from the repository root:

```bash
bash scripts/run-local.sh
```

Open the local endpoints at `http://127.0.0.1:5080/health/live` and `http://127.0.0.1:5080/openapi/v1.json`. Stop the server with `Ctrl+C`.

To run an actual loopback HTTP startup check and end-to-end demo after building, use:

```bash
dotnet build ABA.Net10.Phase1.slnx -c Release
bash scripts/verify-http.sh
```

Alternatively, start manually:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5080 \
dotnet run --project samples/Academy/Host
```

The API is intentionally **not authenticated**. Bind to loopback only and never expose it publicly. The server refuses non-Development environments. Data is saved in `samples/Academy/Host/local-data/` or `ACADEMY_DATA_DIR`.

- Health: `GET http://127.0.0.1:5080/health/live`
- OpenAPI specification: `GET http://127.0.0.1:5080/openapi/v1.json`
- Course catalog: `GET /api/v1/catalog/courses`
- Register user: `POST /api/v1/users` body `{ "email": "alice@example.org" }`
- Read user: `GET /api/v1/users/{id}`
- Request enrollment: `POST /api/v1/enrollments` body `{ "userId": "...", "courseId": "..." }`
- Capture simulated payment: `POST /api/v1/payments/capture`, header `Idempotency-Key: your-key`, body `{ "enrollmentId": "..." }`
- Read enrollment: `GET /api/v1/enrollments/{id}`
- Accounting journals: `GET /api/v1/accounting/journals`
- Reporting: `GET /api/v1/reporting/summary`

Run the deterministic end-to-end HTTP script after startup:

```bash
bash scripts/demo-http.sh
```

The seeded course ID is `dc1311ec-bbd1-4ed7-a18c-886ff51f41b3`, price **USD 19.99** and capacity **two seats**. The fake payment provider does not charge money.

## Optional Docker Compose

With Docker installed, from the repository root:

```bash
docker compose -f infra/compose.yaml up --build
```

The API binds to `127.0.0.1:8080` on the host. The Compose volume persists the local demo state. This is **not** a production deployment and uses no external database or message broker yet.

## Why this layout follows ABA

The PDF emphasizes ownership over folder names and says only **Contracts** are public across bounded contexts (Chapters 6–9, 16, 19–24, 41–42 and 53–55). The sample uses separate Contracts projects plus private context implementations. Other contexts depend only on versioned public Contracts, never on another context's Domain or Infrastructure project. The Host is the composition root and wires the implementations without taking ownership of their business decisions.

`src/Framework/` contains only technical foundation, local messaging, and local read caching; it cannot reference Academy business concepts. Context `Domain/` source files may depend on `Aba.Foundation` but not messaging/persistence or other contexts.

See `docs/ABA-COMPLIANCE-MATRIX.md`, `docs/architecture.md`, `AGENTS.md`, and each context's `_Docs/` directory for enforceable rules and human explanations. The `.yaml` metadata files use **JSON syntax, which is valid YAML 1.2**, to allow dependency-free validation with Python's standard library.

## Warning: local durability is not production durability

The local store uses atomic same-filesystem rename of a serialized envelope containing context state + inbox + outbox. This proves the transactional design **only for a single-process local example**. It does not provide multi-process locking, a real database transaction, external broker guarantees, backup/recovery, or isolation between tenants. The payment provider is fake and has no settlement/reconciliation integration. Do not deploy this project to the public internet or connect real payments.

## Next milestone

Replace the local data adapters with **PostgreSQL/EF Core 10 and Wolverine's supported durable messaging/outbox**, add Redis, OAuth/OIDC via a standards-based identity provider, generated OpenAPI, observability, xUnit/Testcontainers, and `dotnet new` templates—while preserving versioned Contracts and governance. See `docs/NEXT-MILESTONES.md` for sequence and acceptance gates.

## Agent-guided task workflow in every new API

Generated projects now contain `tools/aba_task.py`, `docs/ai/TASK-PROTOCOL.md`, and a local ABA Engineering Skill. Run `python3 tools/aba_task.py plan --task "Change an API" --file src/MyCompany.MyApi.Api/Program.cs` to get a document/ownership/test plan, then `python3 tools/aba_task.py verify --level build` for explicit evidence. Unknown business ownership is flagged instead of guessed. Every new repository also includes a default GitHub Actions build/test workflow and a manual real-service integration workflow.
