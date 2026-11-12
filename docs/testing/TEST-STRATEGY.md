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
