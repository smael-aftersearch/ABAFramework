## New default API starter test suite (verified)

`templates/aba-api/tests/Aba.Starter.Api.Tests` is a **real xUnit project**, copied into each fresh API. Run its project directly; the generated solution also lists service integration tests for Solution Explorer, but they need disposable infrastructure. The root solution groups the optional Academy example, reusable framework and smoke tests under `/Academy/`, `/Framework/` and `/Tests/`. The [2026-11-12 CI run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43286946630) verified 10 cases and the generated app's HTTP/Scalar/OpenAPI. The older `Aba.SmokeTests` console tests below belong solely to the optional Academy sample.

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

The audit verifier now uses ordered async EF row streams for both
ordinary full-chain and separately signed historical-prefix checks,
instead of allocating an entire tenant history as a list. The
`StreamingAuditVerificationPreservesSignedPrefixAndTenantBoundaryAfterTamper`
regression runs on both PostgreSQL and SQL Server: success after 32
technical events, independent historical anchor, negative mid-chain
tamper and preserved neighbor tenant head. Only generated tenant audit
rows are deleted by the test. This is authored evidence pending exact
PR-head CI, not immutable Production audit custody.

## Optional RabbitMQ transport and broker outage test

The generic template's separately built `RabbitMqIntegrationTests.cs` uses a
random technical queue and real RabbitMQ plus both SQL providers. Duplicate
payload deliveries must reach the durable inbox while producing one technical
effect receipt. `BrokerOutageDrill` is excluded from ordinary full verification;
an explicit CI job pauses only the exact disposable, labeled RabbitMQ service,
checks the effect does not arrive while paused, then resumes it and verifies
durable outbox delivery. The test accepts only a loopback URI, an exact container
ID, the pinned test image and the `aba.test.disposable=true` label, and restores
the broker in `finally`. This does not prove production topology or a business
consumer's external side-effect behavior.

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

## Caching and OCI supply-chain regression cases

The generic starter now contains CacheSingleFlightTests (same-key
concurrent request dedup, tenant/key isolation, TTL expiry and
cancellation/fault cleanup). The external SQL/Redis test suite
adds LiveRedisCollapsesConcurrentMissesAcrossRequestScopes with
12 real Redis callers; RedisRestartDrillTests runs in an isolated
job that stops and restarts only its own disposable Docker
Redis 7 container, with negative CI/loopback/container-ID gates.
No ordinary unit or SQL test is permitted to stop Redis.

The generated API xUnit `AbaRedisHealthCheckTests` exercises readiness
probe cleanup on success, failed read and wrong readback, plus fail-closed
cleanup failure and isolation of unrelated tenant cache keys. This is
synthetic provider-failure testing, not a production Redis recovery drill;
count it as executed only after the new PR-head CI succeeds.

`AbaCacheDistributedCoordinationTests` covers opt-in settings, safe
read-after-lease behavior, local fake-coordinator failure and disabled
readiness. `AbaDistributedCacheCoordinationIntegrationTests` uses separate
service providers against shared Redis to cover cross-host same-key collapse,
concurrent and fixed-window source budgets, namespace settings drift, and
unavailable-coordinator fail-closed behavior. The live cases are authored
but require a disposable Redis service; only the unreachable-Redis negative
case has run locally so far.

tools/test_oci_base_lock.py deliberately changes source registry,
digest, tag, stages, runtime UID and missing lock and requires
rejection before Docker build. Root CI signs the scanned Docker
image archive only on pushes to main and rejects modified bytes;
generated products inherit only their own lock verification
and scanner, not upstream signatures.

## CI-only OTLP Collector end-to-end

`scripts/verify-otel-collector.sh` starts a versioned OpenTelemetry Collector
with the confined `infra/otel-collector-ci.yaml` OTLP/gRPC receiver, generates
a fresh API, enables instrumentation in Development and requires the Collector
to actually observe both `ResourceSpans` and `ResourceMetrics` with the
`Pilot.Collector` service identity and HTTP server request metric. A synthetic
sensitive header must not appear in captured telemetry. Docker maps receiver
port 4317 to the GitHub runner loopback only. `scripts/test_otel_collector_gate.py`
proves the drill cannot run without both `CI=true` and
`ABA_ENABLE_OTEL_COLLECTOR_DRILL=yes`. The real collector job is a
prerequisite for main-only signed provenance. PR CI must pass before this
is described as verified; no production collector/credentials are deployed.

## Queue A bounded disposable load probe

`python3 scripts/test_ci_loopback_load.py` tests success/count/concurrency
invariants and rejects non-loopback targets, redirects, non-200 responses,
unbounded counts and missing CI opt-in. On the GitHub `otel-collector` job,
the disposable generated API is additionally exercised by
`scripts/ci_loopback_load.py`; numerical p50/p95, aggregate failures and
throughput are uploaded as `aba-loopback-load-baseline`. This is a CI-only
single-process liveness diagnostic, not a production capacity/SLO claim.
The evidence writer accepts only fresh `artifacts/load/<safe-name>.json` paths,
refuses traversal, absolute paths, existing files and symlink outputs before
sending requests, and uses exclusive creation with private mode `0600`.
The additional happy/negative/invariant tests in the same Python suite check
that prior evidence and unrelated files are never overwritten. These are
local/CI technical security controls, not approved production telemetry storage.

## Generic OTLP exporter URL safety regression (Queue A)

`AbaPlatformTests.OtlpEndpointRejectsEmbeddedSecretsAndNonRoutingUrlParts`
rejects credentials, query and fragment values for both Development and
non-Development configurations; `SafeOtlpUrlAndDisabledDefaultRemainUnchanged`
keeps HTTPS paths and the off-by-default invariant. The existing HTTP-in-
Development/HTTPS-only-in-production test remains. Run the generated .NET 10
xUnit suite via `bash scripts/verify-starter.sh` in CI; authored cases are
not evidence of PASS until that exact PR HEAD finishes successfully.

## Queue C — dual-stack anonymous quota identity regression

`RateLimitTests.Ipv4MappedPeerCannotResetAnonymousQuotaOrSpoofTenant`
checks the actual local fixed-window limiter with one TCP peer expressed
as IPv4 and IPv4-mapped IPv6. Forged `X-Tenant-ID`, forwarded address and
unauthenticated claims must not open a second slot. Native IPv6 and
validated unique JWT subjects remain distinct; health stays exempt.
The optional shared Redis gate consumes the same partition function;
Gateway/Redis ACL and Production approvals remain separate. Exact-head .NET 10
CI must pass before declaring the new test verified.

## Queue A final bounded loopback target regression

`scripts/test_ci_loopback_load.py` exercises canonical `127.0.0.1` input,
negative raw CR/LF/TAB and noncanonical URL spellings, and the invariant that
rejected targets send no HTTP requests. The URL parsing guard protects only
disposable CI loopback diagnostics; it is not a load SLO, production probe or
an authorization to send traffic to a real deployment. See issue #3 for
exact-HEAD PR CI and completion evidence.

## Queue C — tenant SQL connection-open failure boundary

`TenantRlsPolicyTests.FailedOpenClosesPartiallyOpenedTenantConnectionBeforeItCanBeReused`
uses a disposable DbConnection that transitions to Open then faults during
`OpenAsync`. The SQL scope must close the partially opened connection and
never invoke the command callback. `InvalidTenantCannotReachSqlEvenWhenProviderCouldOpen`
denies missing or malformed tenant IDs before connection access. Existing real
PostgreSQL/SQL Server `RowLevelSecurityTests.RlsDeniesForeignRowsForRawSqlAndEfBulkMutations`
remain the positive own-tenant, negative cross-tenant and no-residual-session
acceptance gate. These are technical guards; real tenant memberships, DB
privileges, provider-level session spoofing and production migrations still
need owner/DBA/Security approval. Count the new cases as executed only after
exact-HEAD CI verifies them.

## Queue C final — pre-cancelled tenant SQL scope safety

`TenantRlsPolicyTests.CancelledRequestNeverOpensTenantConnectionOrRunsCallback`
models an already-cancelled request with a provider fake that would otherwise
open even with a cancelled token. The Platform scope now refuses provider
access before OpenAsync (negative), while existing valid-tenant provider and
real SQL-engine tests remain the positive path and missing/malformed tenant
claims still reject before OpenAsync. The invariant is zero opens, closes,
transactions and callbacks for a pre-cancelled request. The authored test is
not a PASS claim until [PR #28 exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/28/checks) verifies it. No production schema, membership or RLS policy is changed.

## Queue B run 9 — malformed query cache payloads

`CacheSingleFlightTests.ExplicitFallbackRepairsOnlyTheCorruptTenantsQueryPayload`
primes another tenant's same-named query key, corrupts a JSON entry,
then verifies an explicitly opted-in authoritative refresh repairs the
scoped entry once while the other tenant's value stays unchanged
(positive and tenant-isolation invariant).
`StrictOrCancelledCorruptCacheReadNeverCallsSourceOrOverwritesEvidence`
requires a strict cache read to propagate `JsonException`, an
already-cancelled fallback to stop before source I/O, malformed bytes
to remain unchanged, and local gate references to be released
(negative and no-side-effect invariant). Real Redis restart and
source-load saturation tests remain independent. The new xUnit cases
must be checked against the exact [PR #29 HEAD](https://github.com/smael-aftersearch/ABAFramework/pull/29/checks)
before PASS is claimed. No business tenant membership or production
cache authorization is inferred.
