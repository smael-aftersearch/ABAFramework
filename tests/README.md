## New default API starter test suite (verified)

`templates/aba-api/tests/Aba.Starter.Api.Tests` is a **real xUnit project**, copied into each fresh API. Run `dotnet test` in the generated solution. The [2026-11-12 CI run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43286946630) verified 10 cases and the generated app's HTTP/Scalar/OpenAPI. The older `Aba.SmokeTests` console tests below belong solely to the optional Academy sample.

---

# Tests in this repository

Read `docs/testing/TEST-STRATEGY.md` before changing behavior. It defines the **current test inventory**, target layout, change-to-test matrix, and mandatory AI test-evidence protocol.

## Executable today (if requirements are installed)

- `Aba.SmokeTests/Program.cs`: a **console** smoke runner, not an xUnit test project; invoke `dotnet run --project tests/Aba.SmokeTests -c Release` with the .NET 10 SDK. It covers the fake-payment enrollment flow, waitlist, accounting invariants, local message delivery/replay and persistence. Build/runtime not verified in the original authoring environment.
- `../scripts/test_governance.py`: run with Python 3; deliberately introduces forbidden architecture states and verifies rejection.
- `../scripts/test_ai_check.py`: run with Python 3; validates that missing or incorrect AI documents are detected.
- `../scripts/governance.py`: run with Python 3; checks repository structure, metadata, dependency boundaries and contracts.
- `../scripts/task_protocol.py plan --task "..."`: suggests a scope-aware test checklist and related docs; it does not prove coverage or assign ownership.
- `../scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json`: records true PASS/FAIL/NOT_RUN for six Python gates.
- `../scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json`: adds .NET SDK, restore/build, console smoke; nonzero when .NET is missing.
- `../scripts/test_task_protocol.py`: regression/negative tests for the task planner and evidence runner.
- `../scripts/verify-all.sh`: complete verification entrypoint via the evidence runner; .NET required for all stages.

The target xUnit-style Unit/Integration/Contract/Architecture/E2E project layout in the policy is a **proposal**, not a set of existing directories or successful test runs. Do not invent success or coverage metrics.

## Generic NuGet vulnerability and package-inventory regression tests

The default generated solution includes `tools/supply_chain.py` and
`tools/test_supply_chain.py`. The framework runs the same portable
offline negative tests in `.github/workflows/verify.yml`, then checks
actual direct/transitive NuGet advisories against the restored default
solution. The SPDX 2.3 inventory and advisory report are retained as
artifacts. A missing/failed advisory check must never silently pass.
The optional real-service integration project is a separate release gate.

## CI-only database-native recovery and expanded NuGet scan

`BackupRestoreDrillTests.cs` lives in the generated external integration test
project, outside the fast Solution. Its `RecoveryDrill` cases require
`CI=true`, a separate opt-in and exact loopback `aba_ci` fixtures.
They verify PostgreSQL `pg_dump/pg_restore` and SQL Server
`BACKUP/VERIFYONLY/RESTORE` after a deliberate data mutation, plus negative
tests refusing untrusted targets. Ordinary API and external infrastructure
tests exclude this category; CI invokes it separately when the disposable
containers are provisioned. See `templates/aba-api/docs/disaster-recovery.md`.
NuGet CI now audits both the normal Solution and the separate integration
project, with independent SPDX package inventories.

**Actual verification:** [CI 43313417455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455) passed 7/7 disposable provider-native recovery/safety tests and 16/16 regular integration tests; independent NuGet scans produced two manifests with 161 and 104 packages.

## Real provider RLS and release provenance

The default starter xUnit suite includes `TenantRlsPolicyTests`;
the isolated PostgreSQL/SQL Server integration project adds
`RowLevelSecurityTests` for cross-tenant raw SQL, EF bulk mutations,
forged inserts and empty tenant context. These rely on actual database
policies, not EF filters. CI also scans a built runtime image via
Trivy and verifies SLSA provenance for the tested upstream template
package. The generated product inherits its own OCI scan, **not**
the upstream signer.

## Audit ledger and operator-only Wolverine DLQ tests

Template unit tests in AuditLedgerConfigTests.cs cover HMAC configuration,
key rotation and safe opaque fields. DeadLetterOperatorGuardTests.cs
tests that optional operator mode refuses missing durability, audit
or real multi-tenancy. The external SQL integration suite includes
AuditLedgerIntegrationTests.cs and DeadLetterOperatorIntegrationTests.cs
on PostgreSQL and SQL Server, with real durable poison quarantine,
authorized single-envelope recovery and tamper-evident audit checks.
No public DLQ endpoint or customer message transport is created.

**Executed validation:** [CI 43320400760](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760)
passed 96 generated API xUnit, 24 real SQL/Redis/Wolverine integration,
7 recovery/safety cases and signed/verified template package provenance.
Real business poison replay and external checkpoint durability are
not part of this technical test fixture.

## SIGKILL fault-injection and offline HMAC checkpoints

The template now contains AbruptCrashWindowTests.cs, intentionally
excluded from ordinary infrastructure verification. Root CI invokes
the CrashDrill category only with explicit CI/opt-in environment
flags and exactly located disposable aba_ci SQL servers. It creates
and kills its OWN child dotnet test process, checking rollback of
an uncommitted row and recovery of a previously persisted delayed
Wolverine message. This is not a live consumer ACK/fanout or real
business Outbox test.

AuditCheckpointTests.cs and AuditAnchoredCheckpointIntegrationTests.cs
cover separately signed historical checkpoints, wrong-key/tamper
denial and PostgreSQL/SQL Server prefix rollback detection while
allowing later legitimate audit writes. Real offsite immutable
storage remains an operational responsibility.

**Executed source CI:** [43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed 8/8 CrashDrill cases (2 SQL engines x 3 crash windows plus negative/sentinel cases), 26/26 real-service tests, 7/7 backup/safety cases, and 99 generated API xUnit cases. Newer revisions must still pass their own run before being called verified.
