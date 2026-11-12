# ABA API — implementation evidence and remaining work

This file is generated from `docs/implementation-status.json`.
The README matrix and this backlog must be updated together via
`python3 scripts/status_matrix.py --write`, and independently verified by CI.
Implemented = code plus focused tests and a reviewed baseline; Partial = real
technical work exists but a stated acceptance gate is missing; Not implemented
= no implementation in the domain-neutral template. Business examples in Academy
do not count as generic framework features.

The **generic .NET 10 API template** is the primary product. This matrix is separate from the
historical Academy Phase 1 demo below. A feature can be implemented technically while
its **production release gates remain open**. Evidence refers to the last reviewed baseline:
[CI run 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032)
(commit `bffe4efc341a`). New commits must pass their own CI before being treated as verified.

**Tracked: 54** · Implemented: 31 · Partial: 6 · Not implemented: 17.

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
| Messaging | Poison/dead-letter inspection and replay | **Not implemented** | Not implemented | Add reviewed dead-letter administration/replay without bypassing authorization | P0 |
| Messaging | Crash-kill Outbox recovery and duplicate delivery | **Not implemented** | Not implemented | Test kill at commit/publish/ack boundary and recovery on both SQL engines | P0 |
| Messaging | Optional external RabbitMQ/Kafka transport | **Not implemented** | Not implemented | Add a versioned optional broker adapter when actual demand exists | P1 |
| Messaging | Real context-owned events and consumers | **Not implemented** | Not implemented | Implement owner-approved domain contracts, Inbox and cross-context handlers | P0 |
| Caching | Redis distributed cache with TTL and scopes | **Implemented** | StackExchangeRedis adapter with scoped JSON TTL/invalidation | Distributed invalidation and PII audit remain open | P1 |
| Caching | Read-through fallback when Redis unavailable | **Implemented** | Default fail-closed; explicit fresh authoritative query fallback | Cluster reconnect, authorized workloads and load behavior unverified | P1 |
| Caching | Cross-instance invalidation and tenant permissions | **Not implemented** | Not implemented | Add versioned invalidation events with authorized namespace boundaries | P1 |
| Caching | Cache stampede and Redis restart resilience | **Not implemented** | Not implemented | Test reconnect after real Redis restart, TTL races and concurrent miss budget | P1 |
| Observability | OpenTelemetry ASP.NET/HTTP traces and metrics | **Partial** | Optional tracing/metrics instrumentation and OTLP exporter wiring | Prove export to a real collector and enforce PII/sampling policy | P1 |
| Observability | Collector pipeline and export integration test | **Not implemented** | Not implemented | Add isolated OTLP receiver/collector to CI and verify trace/metric export | P1 |
| Observability | Tamper-evident audit trail | **Not implemented** | Not implemented | Define context-owned auditable actions with safe retention/PII policy | P0 |
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
| Deployment | SBOM, vulnerability scans and signed releases | **Partial** | CI-verified NuGet direct/transitive audits plus SPDX graphs, fixable-CRITICAL runtime Trivy scan and keyless signed/verified SLSA provenance for tested .nupkg with tamper rejection | Pin OCI base image digests, approve HIGH/unfixed severity policies, sign actual published OCI/NuGet releases and institute production release/change-control | P0 |
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
| `msg-poison-replay` | Add reviewed dead-letter administration/replay without bypassing authorization — Poison message is quarantined, inspected and safely replayed exactly as intended | Not implemented |
| `msg-crash-window` | Test kill at commit/publish/ack boundary and recovery on both SQL engines — No message loss across abrupt process death and controlled duplicate deliveries | Not implemented |
| `msg-domain-consumers` | Implement owner-approved domain contracts, Inbox and cross-context handlers — Consumer cannot mutate another context's tables; duplicate events are safe | Not implemented |
| `ops-audit` | Define context-owned auditable actions with safe retention/PII policy — Audit cannot be silently rewritten and records actor, action, tenant, outcome | Not implemented |
| `ops-prod-secrets` | Add approved secret store, rotation and least-privilege deployment setup — No plaintext production secret in repo/container/CI logs | Not implemented |
| `dev-release-sbom` | Pin OCI base image digests, approve HIGH/unfixed severity policies, sign actual published OCI/NuGet releases and institute production release/change-control — Release pipeline rejects vulnerable/untrusted dependencies and verifies provenance | Partial |
| `dev-prod-deploy` | Choose target hosting, configure DNS/TLS/secrets and deployment rollback — Production smoke, zero-downtime deploy and rollback drill pass | Not implemented |
| `biz-contracts` | Specify real public V1 schemas, consumers and compatibility evolution — V1 consumers stay compatible; private implementation edges rejected | Partial |
| `biz-contract-tests` | Introduce consumer-driven tests for real event/HTTP contracts — A breaking V1 change fails CI and requires an explicit V2 | Not implemented |
| `biz-endpoints` | Implement real domain entities/endpoints only from approved requirements — Owner-specific happy/negative/invariant and provider integration tests pass | Not implemented |

For exact code paths, tests and the P0/P1/P2 backlog, read
[docs/IMPLEMENTATION-STATUS.md](docs/IMPLEMENTATION-STATUS.md).
Statuses are **maintainer reviewed**, not inferred by the mere existence of a file.
Run `python3 scripts/status_matrix.py --check` to fail CI on mismatched tables.

## Execution backlog and exact acceptance checks

| ID | Owner | Priority | Status | Required acceptance / completion evidence |
| --- | --- | --- | --- | --- |
| `auth-membership` | Identity (business owner TBD) | P0 | Not implemented | Unauthorized member and cross-organization privilege escalation tests |
| `auth-sql-rls` | Owning business context / DBA | P0 | Partial | Direct SQL and EF ExecuteUpdate cannot modify foreign tenant records |
| `auth-raw-sql` | Owning business context / Security | P0 | Partial | Cross-tenant raw SQL and bulk mutation are denied |
| `db-business-migrations` | Actual context owner (TBD) | P0 | Not implemented | Both SQL engines perform context migration without shared business tables |
| `db-backup-restore` | Operations / DBA | P0 | Partial | Recover both SQL engines and confirm data integrity after test failure |
| `msg-poison-replay` | Platform / Operations | P0 | Not implemented | Poison message is quarantined, inspected and safely replayed exactly as intended |
| `msg-crash-window` | Platform / Operations | P0 | Not implemented | No message loss across abrupt process death and controlled duplicate deliveries |
| `msg-domain-consumers` | Business owners (TBD) | P0 | Not implemented | Consumer cannot mutate another context's tables; duplicate events are safe |
| `ops-audit` | Security / business owners | P0 | Not implemented | Audit cannot be silently rewritten and records actor, action, tenant, outcome |
| `ops-prod-secrets` | Operations / Security | P0 | Not implemented | No plaintext production secret in repo/container/CI logs |
| `dev-release-sbom` | Release engineering | P0 | Partial | Release pipeline rejects vulnerable/untrusted dependencies and verifies provenance |
| `dev-prod-deploy` | Operations / Security | P0 | Not implemented | Production smoke, zero-downtime deploy and rollback drill pass |
| `biz-contracts` | Business owners (TBD) | P0 | Partial | V1 consumers stay compatible; private implementation edges rejected |
| `biz-contract-tests` | Business owners (TBD) | P0 | Not implemented | A breaking V1 change fails CI and requires an explicit V2 |
| `biz-endpoints` | Actual business owner (TBD) | P0 | Not implemented | Owner-specific happy/negative/invariant and provider integration tests pass |
| `api-shared-limits` | Operations | P1 | Not implemented | Verify one limit across two replicas under concurrent load |
| `db-concurrency` | Actual context owner (TBD) | P1 | Not implemented | Two updates cannot silently overwrite the same business aggregate |
| `msg-broker` | Platform | P1 | Not implemented | Broker outage, retry and duplicate-delivery integration scenarios pass |
| `cache-multinode` | Platform + business owners | P1 | Not implemented | Two API replicas observe invalidations without cross-tenant leakage |
| `cache-stampede` | Platform / Operations | P1 | Not implemented | No excessive authoritative DB load during simultaneous cache misses |
| `otel-instrument` | Platform | P1 | Partial | Collector receives correlated spans/metrics without secrets |
| `otel-collector` | Platform / Operations | P1 | Not implemented | Collector observes test request traces and identifies service correctly |
| `ops-load` | Operations / Performance | P1 | Not implemented | SLO and recovery objectives measurable under load |

## Implemented or partial: checked-in source / executable tests

| ID | Source files / docs | Executable tests / CI gates |
| --- | --- | --- |
| `api-host` | `templates/aba-api/src/Aba.Starter.Api/Program.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`, `scripts/verify-starter.sh` |
| `api-openapi` | `templates/aba-api/src/Aba.Starter.Api/Program.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`, `scripts/verify-starter.sh` |
| `api-problems` | `templates/aba-api/src/Aba.Starter.Api/Program.cs`, `templates/aba-api/src/Aba.Starter.Api/Platform/AbaErrorHandlingExtensions.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ErrorHandlingTests.cs` |
| `api-correlation` | `templates/aba-api/src/Aba.Starter.Api/Program.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs` |
| `api-health` | `templates/aba-api/src/Aba.Starter.Api/Program.cs`, `templates/aba-api/src/Aba.Starter.Platform/AbaPlatformSetup.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ApiSmokeTests.cs`, `scripts/verify-container-starter.sh`, `templates/aba-api/tests/Aba.Starter.Api.Tests/ReadinessTests.cs` |
| `api-rate-limits` | `templates/aba-api/src/Aba.Starter.Api/Platform/AbaRateLimitOptions.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/RateLimitTests.cs` |
| `auth-jwt` | `templates/aba-api/src/Aba.Starter.Api/Program.cs`, `templates/aba-api/src/Aba.Starter.Api/Platform/StartupSecurityGuard.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/SignedJwtAuthenticationTests.cs`, `templates/aba-api/tests/Aba.Starter.Api.Tests/SecurityGuardTests.cs` |
| `auth-keycloak` | `scripts/verify-keycloak-oidc.sh`, `infra/keycloak/aba-ci-realm.json` | `scripts/verify-keycloak-oidc.sh` |
| `auth-tenant-claim` | `templates/aba-api/src/Aba.Starter.Platform/Security/AbaTenantClaims.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/AbaPlatformTests.cs`, `scripts/verify-keycloak-oidc.sh` |
| `auth-tenant-ef` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaTenantDbContext.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/TenantDatabaseBoundaryTests.cs`, `templates/aba-api/tests/Aba.Starter.Api.Tests/TenantPersistenceTests.cs` |
| `auth-sql-rls` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaTenantRlsPolicies.cs`, `templates/aba-api/docs/decisions/ADR-005-tenant-row-security.md` | `templates/aba-api/tests/Aba.Starter.Api.Tests/TenantRlsPolicyTests.cs`, `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/RowLevelSecurityTests.cs` |
| `auth-raw-sql` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaTenantRlsPolicies.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/RowLevelSecurityTests.cs` |
| `db-transport-tls` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaDatabaseOptions.cs`, `templates/aba-api/src/Aba.Starter.Platform/AbaPlatformSetup.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/AbaPlatformTests.cs`, `templates/aba-api/tests/Aba.Starter.Api.Tests/SecurityGuardTests.cs`, `scripts/verify-container-starter.sh` |
| `db-postgres` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaPersistenceServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `db-sqlserver` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaPersistenceServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `db-technical-migrations` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/Migrations/202611120001_InitialTechnicalStore.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `db-context-scaffold` | `templates/aba-api/tools/new_context.py` | `scripts/verify-starter.sh` |
| `db-backup-restore` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/BackupRestoreDrillTests.cs`, `scripts/verify-platform-services.sh`, `templates/aba-api/.github/workflows/integration.yml` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/BackupRestoreDrillTests.cs`, `scripts/test_starter_template.py` |
| `idem-atomic` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaPersistenceServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `idem-concurrent` | `templates/aba-api/src/Aba.Starter.Platform/Persistence/AbaPersistenceServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `msg-wolverine` | `templates/aba-api/src/Aba.Starter.Platform/Messaging/AbaWolverineServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `msg-ef-outbox` | `templates/aba-api/src/Aba.Starter.Platform/Messaging/AbaDurabilityProbe.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `msg-restart` | `templates/aba-api/src/Aba.Starter.Platform/Messaging/AbaWolverineServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/DurabilityRecoveryTests.cs` |
| `cache-redis` | `templates/aba-api/src/Aba.Starter.Platform/Caching/AbaCachingServices.cs` | `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `cache-outage` | `templates/aba-api/src/Aba.Starter.Platform/Caching/ReadThroughQueryCache.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/ReadThroughCacheTests.cs`, `templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs` |
| `otel-instrument` | `templates/aba-api/src/Aba.Starter.Platform/Observability/AbaObservabilityServices.cs` | `templates/aba-api/tests/Aba.Starter.Api.Tests/AbaPlatformTests.cs` |
| `dev-dotnetnew` | `packaging/ABAFramework.GenericApi.Template.csproj` | `scripts/pack-template.sh` |
| `dev-contexts` | `templates/aba-api/tools/new_context.py` | `scripts/verify-starter.sh` |
| `dev-architecture` | `scripts/governance.py`, `templates/aba-api/tools/new_context.py` | `scripts/test_governance.py`, `scripts/verify-starter.sh` |
| `dev-agent` | `templates/aba-api/tools/aba_task.py`, `templates/aba-api/.agents/skills/aba-engineering/SKILL.md` | `templates/aba-api/tools/test_aba_task.py`, `scripts/verify-starter.sh` |
| `dev-status-matrix` | `scripts/status_matrix.py`, `docs/implementation-status.json` | `scripts/test_status_matrix.py`, `.github/workflows/verify.yml` |
| `dev-xunit` | `templates/aba-api/tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj` | `scripts/verify-starter.sh` |
| `dev-services-ci` | `scripts/verify-platform-services.sh` | `scripts/verify-platform-services.sh` |
| `dev-generated-ci` | `templates/aba-api/.github/workflows/ci.yml`, `templates/aba-api/.github/workflows/integration.yml` | `scripts/test_starter_template.py`, `scripts/pack-template.sh` |
| `dev-container` | `templates/aba-api/Dockerfile`, `templates/aba-api/compose.yaml` | `scripts/verify-container-starter.sh` |
| `dev-release-sbom` | `templates/aba-api/tools/supply_chain.py`, `.github/workflows/verify.yml`, `templates/aba-api/.github/workflows/ci.yml`, `docs/release-integrity.md` | `templates/aba-api/tools/test_supply_chain.py`, `scripts/test_starter_template.py` |
| `biz-contracts` | `templates/aba-api/tools/new_context.py` | `scripts/verify-starter.sh` |

## How to record progress without exaggeration

1. Implement source in its ABA owner, and add success, negative and invariant tests.
2. Run the relevant .NET 10 and real PostgreSQL/SQL Server/Redis/Wolverine
   tests when the capability requires actual infrastructure.
3. Change the feature's status only when the stated acceptance gate is met;
   record exact source paths, tests, remaining gaps and the reviewed CI baseline.
4. Run `python3 scripts/status_matrix.py --write`, then
   `python3 scripts/test_status_matrix.py` and
   `python3 scripts/status_matrix.py --check`. CI must pass.
5. Archive historical evidence; do not erase Academy limitations or describe
   a successful technical probe as production proof.

