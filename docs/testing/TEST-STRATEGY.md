> **Latest generic API verification (2026-11-12):** [five successful CI jobs](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43298754432) cover 44 API xUnit cases, 13 real service tests on PostgreSQL + SQL Server + Redis + Wolverine, real Keycloak OIDC/JWKS token authorization, non-root Docker startup, NuGet template installation, and generated-context governance. Historical Academy Phase-1 tables below describe the *optional sample only*; don't interpret them as the current generic template status. See `docs/verification/2026-11-12-hardening.md`.

> **Generic platform CI (2026-11-12):** [verified run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904) generated a domain-neutral API, built .NET 10, ran xUnit, applied EF Core migrations on both PostgreSQL and SQL Server, checked Redis and processed a Wolverine technical probe on both engines. This does **not** cover crash/restart/replay, business outbox atomicity, signed JWT/OIDC end-to-end or tenant row isolation; see `docs/verification/2026-11-12-platform-providers.md`. The historical table below applies to Academy Phase 1.

> **Current additional test suite:** The generic `templates/aba-api` template now ships a genuine xUnit/WebApplicationFactory project (`tests/Aba.Starter.Api.Tests` *inside the template*), whereas the optional original Academy suite remains a console smoke runner. [GitHub Actions](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43286946630) generated a brand-new `Pilot.QuickStart` solution and verified **10 xUnit cases passed**, generated OpenAPI, local health and interactive Scalar UI. The existing “Phase 1” historical table below describes the Academy sample only. See `docs/verification/2026-11-12-generic-starter.md`.

# ABA .NET 10 testing strategy and agent contract

**Status:** policy for the current starter and target testing architecture, last reviewed 2026-11-12. A planned test is **not** an implemented or executed test.

## Purpose and source of truth

Testing is part of the architecture, not an optional last step. Every material feature or bug fix must deliver **code + executable tests + affected documentation + evidence**. Read `AGENTS.md`, this policy, the owning context's `_Docs/` metadata, the relevant implementation and existing tests **before modifying behavior**. Prefer the smallest test that enforces the owning context's rule; use cross-context integration tests for published contracts and actual event flows.

The ABA book's Chapter 19 (Executable Governance), Chapter 20 (Architecture Tests), Chapters 39/54 (CI enforcement), and Chapters 23-24/55 (agent-readable metadata) are the architectural rationale. The test framework choices below are implementation decisions, not requirements imposed by the book.

## What exists in Phase 1 (do not misrepresent)

| Location | Kind | Behavior exercised | Execution status in original authoring environment |
| --- | --- | --- | --- |
| `tests/Aba.SmokeTests/Program.cs` | C# console smoke assertions, **not xUnit** | Paid enrollment, payment idempotency, duplicate delivery, durable local state, retry/dead-letter, waitlist, journal balance and reporting | Authored; .NET runtime **not available**, not executed |
| `scripts/governance.py` | Python architecture validator | Dependency edges, context ownership, contracts and API document structure | Executed, passed |
| `scripts/test_governance.py` | Python negative governance tests | Forbidden cross-context reference, missing ownership metadata, unversioned contract | Executed, passed |
| `scripts/ai_check.py` and `scripts/test_ai_check.py` | AI-documentation validator and tests | Valid skill/knowledge map/metadata; deliberately broken files rejected | Executed, passed |
| `scripts/task_protocol.py` and `scripts/test_task_protocol.py` | Python test-impact planner, fixed-command QA runner and negative regression tests | Candidate owner/test recommendations and real execution evidence | Implemented in this update; Python tests executed |
| `scripts/static_review.py` | Basic structural scan | C# brace/solution shape and metadata (not compilation) | Executed, passed |
| `.github/workflows/verify.yml` | CI definition | Governance + source build + console smoke; HTTP startup smoke added after initial green CI | GitHub Actions initial build/smoke success verified; HTTP test depends on subsequent CI results |
| `scripts/verify-all.sh` | Local verification entrypoint | Above checks + `dotnet restore`, `dotnet build`, `dotnet run` smoke | Original authoring environment blocked; GitHub Actions verified .NET restore/build/smoke |

There are **no** xUnit/NUnit projects, real PostgreSQL/Redis/Wolverine integration tests, API authorization tests, contract snapshot tests, formal code-coverage report, load tests, or end-to-end Docker tests in Phase 1. Do not claim these exist.

## Target structure (proposed, NOT yet implemented)

```text
tests/
  Aba.SmokeTests/                        # EXISTS: temporary end-to-end console suite
  Unit/
    Aba.Foundation.UnitTests/            # PLANNED
    Academy.Identity.UnitTests/          # PLANNED
    Academy.Enrollment.UnitTests/        # PLANNED
    Academy.Payments.UnitTests/          # PLANNED
    Academy.Accounting.UnitTests/        # PLANNED
    Academy.Reporting.UnitTests/         # PLANNED
  Integration/
    Academy.Api.IntegrationTests/        # PLANNED: WebApplicationFactory, HTTP + auth
    Aba.Persistence.IntegrationTests/    # PLANNED: PostgreSQL, migrations, transactions
    Aba.Messaging.IntegrationTests/      # PLANNED: Wolverine outbox/inbox, broker faults
    Aba.Caching.IntegrationTests/        # PLANNED: Redis keys, invalidation, outage
  Contracts/
    Academy.Contracts.Tests/             # PLANNED: event/API backward compatibility
  Architecture/
    Aba.ArchitectureTests/               # PLANNED: .NET assembly tests in addition to Python
  EndToEnd/
    Academy.EndToEndTests/               # PLANNED: realistic HTTP / worker sequence
  Performance/
    Aba.PerformanceTests/                # PLANNED: baseline before setting thresholds
```

Use xUnit or an equivalent modern .NET test runner for future .NET test projects and Testcontainers for real infrastructure in CI. Verify current .NET 10/package compatibility before adding dependencies. Do not add empty project shells solely to improve the file count. Do not migrate the existing console suite until its assertions are preserved in verified replacement tests.

## Required test matrix by change type

| Change | Minimum test expectation | Additional failure/invariant cases |
| --- | --- | --- |
| Domain rule / aggregate | Focused owner-context unit tests | Invalid transition, duplicate, boundary values, invariant remains intact |
| Application command/query | Handler/flow tests with controlled external dependencies | Validation, not found, concurrency/conflict, cancellation as relevant |
| Public `V1`/`V2` contract | Producer serialization/compatibility and consumer tests | Old consumer compatibility, duplicate/out-of-order event, version change governance |
| Cross-context integration or Outbox/Inbox | Transactional integration test using real DB/broker when implemented | Publish failure, duplicate deliveries, retry/dead-letter/replay, crash/restart |
| Database/migration | Real persistence tests and migration rollback/forward check | Transaction rollback, constraints, race conditions, owner isolation |
| API/authentication/authorization | Route, request/response, OpenAPI, authenticated/unauthenticated cases | 400/401/403/404/409, rate limit, tenant isolation, no data leakage |
| Redis/cache | Cache hit/miss and invalidation behavior | TTL expiry, cross-tenant key isolation, Redis outage fallback |
| Governance / AI policy / metadata | Validator pass + negative test demonstrating rule failure | Missing/malformed metadata, forbidden dependency, wrong public contract |
| Pure documentation edit | Relevant docs/AI validation only | No unnecessary production or security side effects |

In critical payment/accounting workflows add tests for incorrect amounts, duplicate callbacks, journaling balance, immutable posted journals, and idempotency keys. Never hit a real payment provider in CI. Prefer reproducible data, fixed clocks, and isolated ephemeral infrastructure.

## Agent task protocol: test first, test together, prove it

1. **Read and classify:** Determine the owning context, contracts, touched APIs, infrastructure and the test categories above. Read `tests/README.md` and the nearest existing tests. Identify implemented vs planned test tooling.
2. **Define acceptance cases before coding:** Write a compact test plan containing a successful case, at least one relevant failure/negative case, and affected domain invariants. For bugs, add a reproduction test first when practical. Record what cannot be tested and why.
3. **Implement in context:** Create or update tests alongside the owner-context code; never let an adjacent context bypass a public Contract just to simplify testing. Do not weaken/delete existing assertions to obtain a green build.
4. **Verify narrowly then broadly:** Run relevant focused tests first; run governance/AI checks and the full suite when tools are available. Introduce a negative architecture fixture if adding a new enforcement rule.
5. **Record evidence:** Report each command and `PASS`/`FAIL`/`NOT RUN`; distinguish static Python checks from C# compilation and runtime tests; never invent coverage, success, test counts or skipped outcomes.
6. **Update docs and delivery status:** Update `docs/ai/INDEX.md`, context metadata, contracts, ADRs, OpenAPI, README, verification report only where affected. Include limitations and risks. A code change without appropriate tests or an explicit limitation is not complete.

## Commands that actually exist today

From the repository root:

```bash
python3 scripts/ai_check.py check
python3 scripts/test_ai_check.py
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py plan --task "add refund event"
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json
bash scripts/verify-all.sh
# After a Release build, verify actual loopback HTTP startup and API demo:
bash scripts/verify-http.sh
```

With .NET 10 SDK, `verify-all.sh` delegates to the QA evidence runner; with .NET 10, it also runs:

```bash
dotnet restore ABA.Net10.Phase1.slnx
dotnet build ABA.Net10.Phase1.slnx -c Release
dotnet run --project tests/Aba.SmokeTests -c Release
```

`dotnet test` is **not** the runner for the existing console smoke suite. Only introduce `dotnet test` once real .NET test projects are added and included in the solution.

## Delivery / CI gates

- **Now:** AI-documentation validation, architecture governance and negative rules, static structure check, .NET build and C# smoke in GitHub Actions. All are expected to pass for a verified CI run; the existence of a workflow file alone is not evidence.
- **After infrastructure is implemented:** Add unit, contract, real persistence/messaging/cache, API/auth and E2E jobs with reproducible setup/teardown. Make relevant tests mandatory in PR checks; surface logs and diagnostics.
- **Before production readiness:** Demonstrate negative authorization and tenant-isolation cases, migration/restore, retry safety, security scans, monitoring, resilience and performance baselines. Do not silently mark missing categories as passed.

## Required verification report for every material AI task

```text
Test impact / owner:
Acceptance cases added/changed (happy/negative/invariant):
Test files created/modified:
Commands actually executed and exact PASS/FAIL/NOT RUN outcomes:
Known unverified areas, blockers and risks:
```

Architecture and test coverage are related but not interchangeable: an ABA dependency checker cannot prove correct financial calculations, and an E2E test cannot prove that a hidden cross-context dependency is absent.

## Generic API — isolated native provider recovery tests (2026-11-13)

`templates/aba-api/tests/Aba.Starter.Platform.IntegrationTests/BackupRestoreDrillTests.cs`
uses real PostgreSQL and SQL Server in CI. Positive acceptance: create only a
disposable database, backup a marker, mutate it, restore with each provider's
native tool, and assert the old marker returns. Negative acceptance: reject
remote/unrecognized database connections and missing CI opt-in. The default
integration suite excludes `Category=RecoveryDrill`; root CI explicitly opts in
with disposable service containers. Real production recovery is **not** tested.
The NuGet supply-chain gate separately audits the external test project's
dependency graph and retains its own advisory/SBOM evidence.

**Verified provider-native drill evidence:** [CI 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455) passed 7/7 isolated SQL/PostgreSQL recovery and negative safety cases, 16/16 existing real-service integration tests, and 16/16 offline supply-chain regressions. Both independent NuGet project graphs were audited.

## Provider database RLS and OCI/package security negative gates

`TenantRlsPolicyTests.cs` verifies PostgreSQL/SQL Server policy SQL,
invalid identifiers, negative tenant scope and migration scope.
`RowLevelSecurityTests.cs` exercises real PostgreSQL/SQL Server,
rejecting cross-tenant direct SQL, ORM `ExecuteUpdate` and
`ExecuteDelete`, forged inserts, and unbound/leftover sessions.
A non-superuser PostgreSQL role avoids silently bypassing RLS.

A commit-pinned Trivy action scans the final runtime image and must
reject fixable CRITICAL findings. The NuGet template SLSA provenance
job, gated on upstream success, must validate a signed artifact and
reject a modified same-name artifact. Only passing CI jobs count as
executed evidence; these do not approve a production release.

## Audit and poison-envelope integration coverage

New default xUnit cases prove disabled defaults, HMAC key length/format
rejection, input validation, key rotation and no phantom operator
permissions. Real PostgreSQL/SQL Server cases exercise a serializable
audit HMAC chain, compromised record detection, retained historic
keys, off-DB digest verification and simultaneous monotonic appends.

A separate Wolverine test on each SQL provider injects a synthetic
poison probe, waits for durable dead-letter quarantine, checks
anonymous and type-mismatched operator denial, repairs a technical
dependency, requests exactly one audited replay and verifies the
eventual receipt plus HMAC chain. It never calls real business handlers.
CI must complete before any such test is claimed successful.

**Verified HMAC and dead-letter tests:** [run 43320400760](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760)
passed all 24 real-service tests and 7 recovery/safety tests, including
PostgreSQL/SQL Server audit concurrency, tampering and rotation,
and each provider's poison quarantine, repaired replay and
pre-committed HMAC operator audit.

## OS-process SIGKILL and independent audit checkpoint tests

AbruptCrashWindowTests (Category=CrashDrill) is isolated from the
normal external suite. With CI=true, explicit disposable-drill opt-in,
exact loopback aba_ci database and unique marker, a parent xUnit
process launches an independent dotnet test host. It kills that
specific child after a SQL row has been flushed but not committed,
then proves the DB rolled it back. A second child is SIGKILLed
after Wolverine ScheduleAsync returns but before its delayed
message is due; a restarted host must process that durable
message. Both phases run on PostgreSQL and SQL Server. Negative
tests reject remote targets, wrong databases and missing opt-in.

AuditCheckpointTests signs an independent historical checkpoint
with a separately held HMAC key and rejects changes to tenant,
digest, sequence, key ID and timestamp. Actual SQL provider tests
confirm a valid prefix survives later appends and cannot be deleted
or rewritten without failing signed-prefix verification. These
tests do not certify immutable offsite storage or a real business
Outbox publish/ACK crash boundary. Use actual CI run evidence
before claiming successful execution.

A third isolated CrashDrill mode parks a synthetic Wolverine
handler after a separately committed idempotent effect but
before its ACK. On process kill/restart, a second delivery
attempt is required while the effect remains unique. This
specifically exercises the at-least-once/duplicate-delivery
ACK boundary under real process death. The generated Product
still needs proof for its own handler and external side effects.

**Execution update:** [all eight CI jobs passed](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289), with 99 default API xUnit cases, 26 live-service cases, 7 native recovery cases and 8 SIGKILL CrashDrill cases. The three crash boundaries are pre-COMMIT rollback, post-scheduled-durable persistence and post-technical-effect/pre-ACK redelivery. A later two-test negative startup safety addition must pass its own CI; this run is not Proof of Production business side effects.

## Real Redis outage/reconnect and single-flight miss budget

The generated fast xUnit CacheSingleFlightTests starts 24 concurrent
request scopes and demands exactly one loader for one scoped key,
no cross-tenant/key contention, cancelled waiter removal and
fault/TTL recovery. The separate live Redis provider test starts
12 concurrent independent scopes and requires one backend loader.
A dedicated RedisRestartDrill CI job actually stops and restarts
the *job-owned* redis:7-alpine container: strict mode must fail
without invoking a loader, explicit authorized fallback must
return source data, and the same Redis client must reconnect and
pass readiness plus tenant-isolated cache hits. Untrusted
container IDs, remote hosts or missing CI opt-in fail closed.
Standard external tests exclude RedisRestartDrill.

## Generic RabbitMQ and Kafka transport tests

`RabbitMqIntegrationTests.cs` and `KafkaIntegrationTests.cs` route only a
synthetic technical probe through a random test queue/topic and check duplicate
payload delivery against both SQL providers. The separately gated RabbitMQ
`BrokerOutageDrill` accepts only a loopback endpoint and the exact disposable,
labeled `rabbitmq:4.4.0-management-alpine` CI container; it pauses that broker,
checks that a durable outbox effect is not delivered while paused, always
unpauses it, then verifies delivery. Ordinary full verification excludes the
container pause operation and the workflow invokes it explicitly. Exact-head
CI must execute these tests before they count as passing evidence. No production
broker topology, retry/dead-letter policy, business consumer, Kafka failover or
external side effect is covered.

## Reviewed pinned OCI bases and attestations

Root and generated CI must validate oci-base-lock.json and both
Dockerfile FROM digests, run negative Python policy regressions
and scan the final image with Trivy. Upstream CI additionally
exports and reloads the scanned Docker image, compares image
IDs, signs the exact Docker-save artifact with SLSA provenance
and requires a mutated same-name archive to fail. Test evidence
must distinguish this archive provenance from signing a
registry-published digest.

## CI loopback-load evidence integrity (Queue A)

The opt-in `ops-load` probe (`scripts/ci_loopback_load.py`) has a bounded
40-request/four-worker loopback-only acceptance case, rejects redirects and
non-200 responses, and is not a production performance SLO. Additional
`scripts/test_ci_loopback_load.py` regressions require a new JSON artifact
(and assert mode `0600` on POSIX) and reject file traversal, arbitrary absolute
paths, existing files and symlink destinations **before any HTTP request**.
On Windows, symlink creation is tested when the host permits it; otherwise
that single platform-specific case is skipped while the other path and request
guards still run. Only a disposable CI runner with the explicit opt-in may
execute the full probe; the generated
application's real `otel-collector` job still provides end-to-end evidence.
