# ABA Framework — .NET 10 generic API quick start

**Verified technical baseline (2026-11-12):** [five passing GitHub Actions jobs](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43302521502) compiled a fresh .NET 10 domain-neutral API, passed **60 xUnit API tests** plus generated-context registration checks and **16 real-service integration tests** (PostgreSQL, SQL Server, Redis and Wolverine). CI also verified actual Keycloak OIDC/JWKS 204/401/403, non-root Docker, installable NuGet package and ABA governance. Improvements in the new template include optional per-context `--with-db` scaffolding, transactional/concurrent idempotency, an explicit source-backed Redis outage path, JWT-partitioned rate limiting and an agent task/evidence runner. This successful baseline includes CI workflows inside generated projects and the installable v0.3 template. The newer SQL TLS and status-matrix changes must pass their own CI before their release status advances. Academy remains an **optional example**, not the base framework. **Production release is still gated.**


**Start here if you want a new, ordinary API.** The domain-neutral `aba-api` template is the primary deliverable. You do **not** need the Academy enrollment/payment example to create your product.

```bash
git clone https://github.com/smael-aftersearch/ABAFramework.git
cd ABAFramework
dotnet new install ./templates/aba-api
dotnet new aba-api -n MyCompany.MyApi -o ../MyCompany.MyApi
cd ../MyCompany.MyApi
dotnet run --project src/MyCompany.MyApi.Api
```

Use .NET 10 SDK. In Development open `http://127.0.0.1:5174/scalar` for the **interactive API reference**, `/openapi/v1.json` for generated OpenAPI, or `/health/live` to check readiness. Run `dotnet test MyCompany.MyApi.slnx` for **real xUnit tests** included in every generated project.

The generated project has no Academy or business examples. It includes a versioned HTTP route, OIDC/JWT startup guard (non-Development requires HTTPS issuer and audience), protected endpoints by default, standard Problem Details, correlation IDs, rate limiting, test cases, architecture documentation and AI agent instructions. See `templates/aba-api/README.md` and `docs/architecture.md`.

**CI verification:** `bash scripts/verify-starter.sh` installs the template, generates a fresh named solution, runs restore/build/xUnit and checks the real local HTTP host. The original Academy sample remains an **optional reference only**, not the base API.

## Installable template package

CI also builds and tests a versioned `ABAFramework.GenericApi.Template.*.nupkg` NuGet template artifact. After downloading it from the successful [GitHub Actions](https://github.com/smael-aftersearch/ABAFramework/actions) run, install with `dotnet new install ./ABAFramework.GenericApi.Template.0.3.0-preview.1.nupkg`, then `dotnet new aba-api -n YourCompany.Api`. This is an artifact attached to CI, **not yet a NuGet.org publication**. Pack locally with `bash scripts/pack-template.sh`.

## Roadmap and scope

**Implemented technical integrations (opt-in):** EF Core 10 for **both PostgreSQL and Microsoft SQL Server**, provider-neutral migrations, relational idempotency receipts, Redis distributed cache with TTL and scope isolation, Wolverine 6 durable local messaging with EF transactional middleware for either SQL engine, and OpenTelemetry traces/metrics with optional OTLP export. Optional authenticated JWT tenant-claim enforcement and context-owned EF Core query/write guards are implemented; real tests on **both** database engines verify denial of cross-tenant EF reads and writes. This does **not** provide SQL-level RLS, trusted tenant membership or protection against raw SQL/bulk operations. All dependencies default to off so the blank API starts immediately.

**Current evidence:** [full generic-framework run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43302521502) verifies 16 real-service scenarios (Wolverine transactional outbox and clean restart on both engines, transactional idempotency, EF tenant protection on both SQL engines, Redis), plus actual Keycloak/OIDC tests, Docker startup, xUnit and template packaging. The module generator creates Domain/Application/Infrastructure/Contracts, owner metadata and tests; `--with-db` adds an optional context-owned EF shell for either SQL engine. **Not certified:** ungraceful process-kill recovery, poison/dead-letter replay, live tenant membership and DB RLS, external broker, migration rollback/backup plans, load/security audit or production deployment.

Setup and connection settings: [generic platform infrastructure guide](templates/aba-api/docs/platform-infrastructure.md). Local optional services: `docker compose -f infra/compose.platform.yaml up -d`. Remaining quality gates: `docs/NEXT-MILESTONES.md`.

---

## Implemented versus not yet implemented

<!-- ABA-STATUS:START -->

The **generic .NET 10 API template** is the primary product. This matrix is separate from the
historical Academy Phase 1 demo below. A feature can be implemented technically while
its **production release gates remain open**. Evidence refers to the last reviewed baseline:
[CI run 43302521502](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43302521502)
(commit `b170e1c7858c`). New commits must pass their own CI before being treated as verified.

**Tracked: 54** · Implemented: 29 · Partial: 4 · Not implemented: 21.

| Area | Parameter / feature | Status | Delivered scope | Remaining task | Priority |
| --- | --- | --- | --- | --- | --- |
| API | Domain-neutral .NET 10 HTTP API | **Implemented** | Versioned Minimal API with composition root | No generic framework work outstanding | P0 |
| API | OpenAPI plus interactive Scalar | **Implemented** | Generated OpenAPI and Development-only Scalar UI | Production API docs need a reviewed access policy | P1 |
| API | Problem Details and HTTP errors | **Implemented** | ASP.NET ProblemDetails and exception handler | Per-context business error taxonomy not yet specified | P1 |
| API | Request correlation IDs | **Implemented** | Validated X-Correlation-ID response header | Correlate logs, traces and message envelopes across services | P1 |
| API | Liveness and readiness | **Implemented** | Separate health/live and health/ready with dependency tags | Prove health behavior during dependency outage and at scale | P0 |
| API | Authenticated per-identity rate limits | **Implemented** | JWT subject + tenant partition; anonymous TCP peer; Health bypass | Multi-node shared quotas require a gateway | P1 |
| API | Distributed multi-instance rate limits | **Not implemented** | Not implemented | Add shared gateway/quota store with trusted proxy policy | P1 |
| Security | OIDC/JWT signature, issuer and audience | **Implemented** | ASP.NET JWT bearer validation and fail-closed production OIDC config | External real-user IdP configuration and security operations remain | P0 |
| Security | Real Keycloak OIDC/JWKS test | **Implemented** | Ephemeral Keycloak CI verifies signed 204, missing/malformed 401, tenant 403 | Real IdP key rotation and organization policy not validated | P0 |
| Security | Trusted tenant claim required when enabled | **Implemented** | JWT tenant claim fallback/default authorization; ignores raw tenant header | Prove organization membership in actual IdP | P0 |
| Security | Tenant membership / RBAC / ABAC | **Not implemented** | Not implemented | Define owning membership model, roles and permissions | P0 |
| Security | EF tenant row query and SaveChanges guards | **Implemented** | Tenant-scoped EF filtering and foreign-row write rejection | Raw SQL, bulk updates and SQL-level RLS remain open | P0 |
| Security | Database-enforced Row-Level Security | **Not implemented** | Not implemented | Plan and verify provider-specific RLS for both databases | P0 |
| Security | Bulk/raw SQL tenant authorization | **Not implemented** | Not implemented | Add trusted tenant-bound access policies beyond EF tracking | P0 |
| Persistence | Production SQL TLS and certificate identity | **Partial** | Startup guard authored for PostgreSQL VerifyFull and SQL Server encryption; new CI not yet reviewed | Verify compile/negative tests, production cert trust and deployment integration | P0 |
| Persistence | EF Core 10 PostgreSQL provider | **Implemented** | Runtime Npgsql provider selected by config | Context-owned schema migrations require real domains | P0 |
| Persistence | EF Core 10 Microsoft SQL Server provider | **Implemented** | Runtime EF SqlServer provider selected by config | Context-owned schema migrations require real domains | P0 |
| Persistence | EF technical idempotency migration | **Implemented** | Provider-neutral checked-in EF technical migration | Production migration rollback/backup remains unverified | P0 |
| Persistence | Optional per-context EF Core scaffold | **Implemented** | --with-db creates empty context-owned DbContext for both SQL engines | No business entities, actual migrations or policies are invented | P1 |
| Persistence | Business context schema and migrations | **Not implemented** | Not implemented | Implement real owner-specific models and migrations after requirements | P0 |
| Persistence | Business optimistic concurrency handling | **Not implemented** | Not implemented | Implement rowversion/concurrency tokens and conflict semantics per context | P1 |
| Persistence | Backup, recovery, rollback and restore drill | **Not implemented** | Not implemented | Build reversible migration and recoverable backup/restore runbooks | P0 |
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
| Developer | Installable versioned NuGet API template | **Implemented** | v0.3.0-preview.1 NuGet template artifact installs successfully | NuGet.org publishing and signed release not done | P1 |
| Developer | Domain/Application/Contracts/Infrastructure generator | **Implemented** | Scaffold real named contexts with ownership docs; no fictional entities | Complete real domain behavior remains owner-dependent | P1 |
| Governance | ABA ownership and forbidden-edge checks | **Implemented** | Repo and generated-project architecture checks with negative cases | Semantic/IL enforcement still requires human architecture review | P0 |
| Governance | Portable agent Skill, task plan and verification | **Implemented** | AGENTS, Skill, task-aware reader, PASS/FAIL/NOT_RUN evidence | AI agent adoption varies by platform; semantic review still manual | P1 |
| Governance | Machine-readable feature status and remaining tasks | **Partial** | JSON catalog/renderer and negative tests authored; first CI pending | Publish synchronized README+backlog tables and enforce CI drift check | P1 |
| Testing | Default xUnit HTTP/authorization/EF tests | **Implemented** | 60+ API xUnit checks in fresh generated solution | Business invariants and load testing need real domains | P0 |
| Testing | Real PostgreSQL/SQL Server/Redis/Wolverine CI | **Implemented** | 16 executable real-service tests across both SQL engines | Crash, high-load and external broker remain release gates | P0 |
| Testing | CI bundled into each generated API | **Implemented** | Push/PR build gate and manually triggered real-service workflow | Generated-project pipelines need repository policy/hardening to be adopted | P1 |
| Deployment | Non-root OCI image and local Compose | **Implemented** | Multi-stage .NET 10 Dockerfile, localhost-only Development Compose | Production image signing, secrets/TLS/hosting still open | P1 |
| Deployment | SBOM, vulnerability scans and signed releases | **Not implemented** | Not implemented | Add pinned image digests, dependency audits, SBOM and artifact attestations | P0 |
| Deployment | Production deployment with TLS, identity and rollback | **Not implemented** | Not implemented | Choose target hosting, configure DNS/TLS/secrets and deployment rollback | P0 |
| Operations | Latency/throughput/load and failover baseline | **Not implemented** | Not implemented | Set SLO and run load/chaos tests across two provider deployments | P1 |
| Business | Owner-defined versioned cross-context contracts | **Partial** | Context generator creates Contracts project; no real product Contracts | Specify real public V1 schemas, consumers and compatibility evolution | P0 |
| Business | Consumer-driven contract compatibility tests | **Not implemented** | Not implemented | Introduce consumer-driven tests for real event/HTTP contracts | P0 |
| Business | Actual product CRUD/workflows and invariants | **Not implemented** | Not implemented | Implement real domain entities/endpoints only from approved requirements | P0 |

### Release-critical tasks still open (P0)

| Task ID | Task / acceptance gate | Current state |
| --- | --- | --- |
| `auth-membership` | Define owning membership model, roles and permissions — Unauthorized member and cross-organization privilege escalation tests | Not implemented |
| `auth-sql-rls` | Plan and verify provider-specific RLS for both databases — Direct SQL and EF ExecuteUpdate cannot modify foreign tenant records | Not implemented |
| `auth-raw-sql` | Add trusted tenant-bound access policies beyond EF tracking — Cross-tenant raw SQL and bulk mutation are denied | Not implemented |
| `db-transport-tls` | Verify compile/negative tests, production cert trust and deployment integration — Non-Development refuses insecure or unverified SQL connection strings | Partial |
| `db-business-migrations` | Implement real owner-specific models and migrations after requirements — Both SQL engines perform context migration without shared business tables | Not implemented |
| `db-backup-restore` | Build reversible migration and recoverable backup/restore runbooks — Recover both SQL engines and confirm data integrity after test failure | Not implemented |
| `msg-poison-replay` | Add reviewed dead-letter administration/replay without bypassing authorization — Poison message is quarantined, inspected and safely replayed exactly as intended | Not implemented |
| `msg-crash-window` | Test kill at commit/publish/ack boundary and recovery on both SQL engines — No message loss across abrupt process death and controlled duplicate deliveries | Not implemented |
| `msg-domain-consumers` | Implement owner-approved domain contracts, Inbox and cross-context handlers — Consumer cannot mutate another context's tables; duplicate events are safe | Not implemented |
| `ops-audit` | Define context-owned auditable actions with safe retention/PII policy — Audit cannot be silently rewritten and records actor, action, tenant, outcome | Not implemented |
| `ops-prod-secrets` | Add approved secret store, rotation and least-privilege deployment setup — No plaintext production secret in repo/container/CI logs | Not implemented |
| `dev-release-sbom` | Add pinned image digests, dependency audits, SBOM and artifact attestations — Release pipeline rejects vulnerable/untrusted dependencies and verifies provenance | Not implemented |
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
