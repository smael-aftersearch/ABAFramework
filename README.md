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

The generated project has no Academy or business examples. It includes a versioned HTTP route, OIDC/JWT startup guard (non-Development requires HTTPS issuer and audience), protected endpoints by default, an explicit CORS origin allow-list, standard Problem Details, correlation IDs, rate limiting, test cases, architecture documentation and AI agent instructions. See `templates/aba-api/README.md` and `docs/architecture.md`.

**CI verification:** `bash scripts/verify-starter.sh` installs the template, generates a fresh named solution, runs restore/build/xUnit and checks the real local HTTP host. The original Academy sample remains an **optional reference only**, not the base API.

## Ten-run hourly delivery tracker (started 2026-11-13)

The [hourly delivery control issue](https://github.com/smael-aftersearch/ABAFramework/issues/6) tracks **23 remaining generic-template capabilities** from [the authoritative status catalog](docs/implementation-status.json). The initial reviewed baseline is **31 implemented, 10 partial, 13 not started** out of 54. A partial feature is **not** counted as completed. Each queue has a maximum of ten executions (one immediate run and nine scheduled hourly runs); missed or blocked production gates are reported, not auto-declared complete.

| Queue / owners | Capabilities still open at baseline | Partial | Not started | Newly completed and verified this batch | Tracking |
| --- | ---: | ---: | ---: | ---: | --- |
| A — release/CI, audit, telemetry, operations | 7 | 3 | 4 | 0 / 7 | [#3](https://github.com/smael-aftersearch/ABAFramework/issues/3) |
| B — persistence, messaging, caching | 9 | 4 | 5 | 0 / 9 | [#4](https://github.com/smael-aftersearch/ABAFramework/issues/4) |
| C — security, API and business contracts | 7 | 3 | 4 | 0 / 7 | [#5](https://github.com/smael-aftersearch/ABAFramework/issues/5) |
| **Total** | **23** | **10** | **13** | **0 / 23** | [Ledger](https://github.com/smael-aftersearch/ABAFramework/issues/6) |

**Queue B — `cache-stampede` incremental resilience (execution 2/10):** [PR #8](https://github.com/smael-aftersearch/ABAFramework/pull/8) adds a fail-fast, per-host limit for opted-in authoritative reads on cache misses and Redis outages, with failure/cancellation and tenant/key-scope regression tests. At the original code head `cabd2c7`, [CI 43338197320](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43338197320) passed **8/8 applicable jobs**, including **116 generated API xUnit tests**, live SQL/Redis/Wolverine integration and the isolated Redis restart. This is an incremental verified **Partial** improvement, **not a closure** of the distributed `cache-stampede` feature (B **0/9**). No business migrations, broker, multi-replica lock or production authorization is inferred.

**Queue B — bounded cache-miss in-flight calls (execution 4/10; 2026-11-13):**
`cache-stampede` adds a **single-host, configurable (default 1024) cap** on
the total registered cache-miss callers, including queued same-key waiters.
Saturation refuses new work before cache/source access, does not retain an
unauthorized tenant key, and cancellation releases space; scoped requests
can retry after capacity returns. Success/denial/invariant .NET tests are
**AUTHORED, NOT RUN** until exact-HEAD CI verifies them. This is not a
multi-node coordination, owner-approved policy, or production readiness claim.
Queue B feature status: `db-business-migrations=not_started`,
`db-concurrency=not_started`, `db-backup-restore=partial`,
`msg-poison-replay=partial`, `msg-crash-window=partial`,
`msg-broker=not_started`, `msg-domain-consumers=not_started`,
`cache-multinode=not_started`, `cache-stampede=partial`.
**Verified closures B 0/9 (0%)**. Owner-specific migrations, concurrency and
business consumers remain **BLOCKED**. Review scoped changes and required
checks in [PR #13 and exact-HEAD CI checks](https://github.com/smael-aftersearch/ABAFramework/pull/13/checks)
and [Queue B ledger #4](https://github.com/smael-aftersearch/ABAFramework/issues/4).

**Queue B — mixed-identity replay denial (`msg-poison-replay`, execution 5/10; 2026-11-13):** The opt-in audited Wolverine dead-letter service now binds both authenticated `sub` and exact `aba_permission` to **one verified claims identity** instead of composing permission/subject from anonymous or competing identities. The existing authorized poison/replay happy case is preserved; new real PostgreSQL and SQL Server integration cases deny split-permission, split-subject and ambiguous identities, and assert no audit append or replay-state change on denial. This is an **authored technical patch; new-HEAD CI remains required**. The prior [main CI 43353408323](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43353408323) passed 10/10 on `21ae4530` but is not evidence for this patch. [Queue B PR #16 and exact-HEAD checks](https://github.com/smael-aftersearch/ABAFramework/pull/16/checks) / [issue #4](https://github.com/smael-aftersearch/ABAFramework/issues/4). All nine B IDs retain their listed states: `db-business-migrations=not_started`, `db-concurrency=not_started`, `db-backup-restore=partial`, `msg-poison-replay=partial`, `msg-crash-window=partial`, `msg-broker=not_started`, `msg-domain-consumers=not_started`, `cache-multinode=not_started`, `cache-stampede=partial`. **B closures 0/9 (0%)**; signed operator trust, tenant authorization, business idempotency and production approvals remain open.

**Queue B — duplicate operator-subject claim denial (`msg-poison-replay`, execution 6/10; 2026-11-13):** The service-only Wolverine dead-letter operator now requires **exactly one `sub` claim inside its single authenticated identity**; conflicting or repeated `sub` values cannot choose the first identity for a signed replay audit. Existing single-subject authorized poison repair and replay remains the happy path. New real PostgreSQL/SQL Server negative tests deny both inspect/replay for conflicting subjects and replay for duplicate same-value subjects; no rejected request can mark the envelope replayable or append an audit event. This additional check must pass new exact-HEAD CI before merge. [PR #19 exact-HEAD checks](https://github.com/smael-aftersearch/ABAFramework/pull/19/checks), [Queue B #4](https://github.com/smael-aftersearch/ABAFramework/issues/4) and [central ledger #6](https://github.com/smael-aftersearch/ABAFramework/issues/6); previous [main CI 43360723689](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43360723689) was green on prior SHA, not evidence for this patch. Queue B IDs: `db-business-migrations=not_started`, `db-concurrency=not_started`, `db-backup-restore=partial`, `msg-poison-replay=partial`, `msg-crash-window=partial`, `msg-broker=not_started`, `msg-domain-consumers=not_started`, `cache-multinode=not_started`, `cache-stampede=partial`. **Verified full closures 0/9 (0%)**; domain ownership, signed operator policy, tenant-aware store access and Production recovery remain blocked or unverified.

**Queue B — Redis health-probe failure cleanup (`cache-stampede`, execution 7/10; 2026-11-13):** The isolated Platform readiness probe now removes its unique temporary key after both healthy and failed readback; failed deletion makes the check Unhealthy rather than hiding a Redis failure. Generated API xUnit cases cover happy path, read timeout, wrong value, cleanup timeout and unrelated tenant-key preservation. This is **authored**, awaiting exact-head PR CI; it is **not** distributed cache coordination, tenant authorization or production readiness. [PR #23 and exact-head CI/review](https://github.com/smael-aftersearch/ABAFramework/pull/23/checks), [issue #4](https://github.com/smael-aftersearch/ABAFramework/issues/4) and [central #6](https://github.com/smael-aftersearch/ABAFramework/issues/6). All nine Queue B ID states remain: `db-business-migrations=not_started`, `db-concurrency=not_started`, `db-backup-restore=partial`, `msg-poison-replay=partial`, `msg-crash-window=partial`, `msg-broker=not_started`, `msg-domain-consumers=not_started`, `cache-multinode=not_started`, `cache-stampede=partial`. Verified complete B capabilities **0/9 (0%)**; production/distributed guarantees and business ownership remain open.

**Queue B — execution 8/10, `msg-crash-window` CI marker boundary (2026-11-13):** The isolated SIGKILL fixture now refuses noncanonical or symlinked crash-marker paths and never overwrites a marker or staged sentinel. The technical crash worker and pre-ACK probe share one disposable-directory gate, with positive marker signal, negative opt-in/path/symlink and unrelated-file preservation tests. This is **AUTHORED, exact-head CI NOT YET VERIFIED**, not proof of a real business Outbox/broker ACK or a production crash policy. Queue B IDs: `db-business-migrations=not_started`, `db-concurrency=not_started`, `db-backup-restore=partial`, `msg-poison-replay=partial`, `msg-crash-window=partial`, `msg-broker=not_started`, `msg-domain-consumers=not_started`, `cache-multinode=not_started`, `cache-stampede=partial`; verified closed **0/9 (0%)**. [Queue B ledger #4](https://github.com/smael-aftersearch/ABAFramework/issues/4), [central ledger #6](https://github.com/smael-aftersearch/ABAFramework/issues/6), and [PR #26 and exact-HEAD CI](https://github.com/smael-aftersearch/ABAFramework/pull/26/checks).

**Queue B — `msg-broker` external transports (execution 9/10 extended on request; 2026-11-13):** The generic Platform now has opt-in Wolverine RabbitMQ and Kafka adapters, owner-provided versioned routes, Development-only topology provisioning, RabbitMQ publisher-confirm tracking and Production transport-security guards. Kafka additionally configures idempotent producers with `acks=all` and requires `SaslSsl` plus supported SASL credentials in Production. Authored tests cover invalid/unsafe settings, duplicate delivery through both brokers against both SQL engines and the separately gated RabbitMQ pause/recovery drill. Local compile and configuration tests pass; live broker tests cannot run locally because Docker Desktop is unavailable and Java is not installed. `msg-broker` remains **Partial** until exact-head full CI and the RabbitMQ outage drill pass. Product handlers/contracts, Production topology and owner-approved retry/dead-letter policy remain unimplemented.


**Queue B — final execution 10/10, cancelled query-cache operations (`cache-stampede`, 2026-11-13):** [PR #30](https://github.com/smael-aftersearch/ABAFramework/pull/30) adds an explicit cancellation check after provider cache reads and after authoritative callbacks, even if a misbehaving adapter ignores the cancellation token. The happy retry, negative cancellation during cache-hit and source-load, tenant isolation, and gate/budget release are covered by `CacheSourceBudgetTests`. The technical source/test HEAD `2c8815ddb0ef` passed [CI 43391409289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43391409289): **158 API tests**, all real-service checks and status-matrix governance. Final documentation-head gates remain visible at [PR #30 checks](https://github.com/smael-aftersearch/ABAFramework/pull/30/checks). Previous [main CI 43384966207](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43384966207) passed 10/10 on `9e3afcd5aecc` **before** this patch. The final queue is closed to additional automatic changes after this run; incomplete owner/production gates are not silently resolved.

| Queue B capability ID | Catalog status | Remaining gate / result |
| --- | --- | --- |
| `db-business-migrations` | Not started · BLOCKED | Owning business context and approved schemas/migrations |
| `db-concurrency` | Not started · BLOCKED | Owner-specific aggregate conflict semantics |
| `db-backup-restore` | Partial | Approved RTO/RPO, business PITR, offsite restore and DBA |
| `msg-poison-replay` | Partial | Trusted operator/tenant permissions and business compensation |
| `msg-crash-window` | Partial | Business Outbox/ACK, external effects and broker fault approval |
| `msg-broker` | Partial | Exact-head duplicate-delivery and isolated broker outage tests; production policies remain open |
| `msg-domain-consumers` | Not started · BLOCKED | Owner-approved versioned contracts and consumers |
| `cache-multinode` | Not started | Authorized cross-replica invalidation protocol |
| `cache-stampede` | Partial | Multi-replica budget/fencing, scope authorization and production topology |

**Queue B complete closures: 0/9 (0%)** — these are technical incremental patches, not completion of nine acceptance criteria. No customer data, production restoration, broker deployment or business contracts changed. Evidence ledger: [issue #4](https://github.com/smael-aftersearch/ABAFramework/issues/4) and [central issue #6](https://github.com/smael-aftersearch/ABAFramework/issues/6).

**Queue C — technical rate-limit progress (execution 2/10; 2026-11-13):**
`api-shared-limits` now has an opt-in Redis Lua quota with **two separately hosted
in-process HTTP apps** sharing disposable Redis, concurrent forged forwarded/tenant
headers, namespace separation, 429 quota and 503 outage cases. The exact PR
head `bae60c60304c432def1287ece9fb5ce4ea242dbf` passed [8/8 PR CI jobs](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43344790754)
(including 120 API xUnit and 3 new real Redis HTTP cases); later heads require
independent green checks. **Partial**, not independently approved for production:
the deployment security owner must approve the proxy, Redis ACL/TLS, and operational policy.
Remaining queue statuses: `api-shared-limits` partial, `auth-membership` not_started,
`auth-sql-rls` partial, `auth-raw-sql` partial, `biz-contracts` partial,
`biz-contract-tests` not_started, `biz-endpoints` not_started.
**Fully verified Queue C closures: 0/7 (0%)**; no fabricated domain memberships or contracts.
[Queue C #5](https://github.com/smael-aftersearch/ABAFramework/issues/5)
and [PR #10](https://github.com/smael-aftersearch/ABAFramework/pull/10) · [exact-head checks](https://github.com/smael-aftersearch/ABAFramework/pull/10/checks)
contain proposed diff and the required CI/review evidence; do not infer a PASS before checks complete.

**Queue C — shared quota transport safety (execution 3/10; 2026-11-13):**
[PR #11](https://github.com/smael-aftersearch/ABAFramework/pull/11) adds an
opt-in fail-closed non-Development Redis transport guard to `api-shared-limits`.
Positive: `ssl=true` is accepted without contacting Redis; negative: plaintext
and explicit `ssl=false` are rejected; tenant-bypass negative: forged
`X-Tenant-ID`/unsigned claims cannot create a second anonymous identity;
invariant: disabled shared quotas need no Redis. Existing live two-host
429/503/spoofing tests remain required. [Exact-head CI and review](https://github.com/smael-aftersearch/ABAFramework/pull/11/checks)
must be green before merge; local .NET checks are **NOT RUN**. This still does not
verify distinct OS processes, production certificate trust/ACLs or gateway
policy. Statuses remain `api-shared-limits=partial`, `auth-membership=not_started`,
`auth-sql-rls=partial`, `auth-raw-sql=partial`, `biz-contracts=partial`,
`biz-contract-tests=not_started`, `biz-endpoints=not_started`.
**Verified Queue C closures: 0/7 (0%); no production approvals inferred.**

**Queue C — distinct-process quota validation (execution 4/10, 2026-11-13):**
`api-shared-limits` now includes a CI-only fixture launching separate generated
.NET HTTP API processes on random loopback ports to verify one atomic quota,
deny forged tenant/forwarded-header bypass and preserve health plus namespace
isolation. Its own safety tests refuse non-CI and off-host Redis.
[Queue C #5](https://github.com/smael-aftersearch/ABAFramework/issues/5)
and [PR #14 exact-head checks](https://github.com/smael-aftersearch/ABAFramework/pull/14/checks)
provide review/evidence; new tests are **NOT RUN until this PR head passes CI**.
`api-shared-limits` remains **partial**, pending Redis certificate/ACL,
gateway and production-scale approvals. Other C statuses stay
`auth-membership=not_started`, `auth-sql-rls=partial`, `auth-raw-sql=partial`,
`biz-contracts=partial`, `biz-contract-tests=not_started`,
`biz-endpoints=not_started`; verified closures **0/7 (0%)**.

### Queue C — authenticated-principal isolation (execution 5/10; 2026-11-13)

Technical identity-boundary patch in [PR #17](https://github.com/smael-aftersearch/ABAFramework/pull/17) ([exact-head checks](https://github.com/smael-aftersearch/ABAFramework/pull/17/checks)) for `api-shared-limits` and `auth-raw-sql`:
the rate-limit subject and tenant claim now must come from the **same single authenticated identity**.
Tenant authorization refuses a split authenticated/unsigned principal or two authenticated
identities; ambiguous quota requests use the TCP peer instead of borrowing an unsigned subject.
New positive, tenant-bypass negative and same-partition/health invariants are in
[`AbaPlatformTests.cs`](templates/aba-api/tests/Aba.Starter.Api.Tests/AbaPlatformTests.cs)
and [`RateLimitTests.cs`](templates/aba-api/tests/Aba.Starter.Api.Tests/RateLimitTests.cs).
The latest previous fully verified [main CI 43356449614](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43356449614)
passed 10/10; the new patch requires its own exact-head PR checks. No independent
membership, business contracts, DBA migration or production policy is inferred.

| Queue C ID | Current status | Gate still outstanding |
| --- | --- | --- |
| `api-shared-limits` | partial | Production gateway, Redis certificate/ACL, load approval |
| `auth-membership` | not_started (BLOCKED) | Named Identity owner and approved role/membership policy |
| `auth-sql-rls` | partial (BLOCKED for deployment) | DBA-reviewed owner migrations/least-privilege roles |
| `auth-raw-sql` | partial | SQL-injection/session spoof defense, business owner and DBA/Security approval |
| `biz-contracts` | partial (BLOCKED) | Owner-approved V1 schemas and consumers |
| `biz-contract-tests` | not_started (BLOCKED) | Real producer/consumer contracts |
| `biz-endpoints` | not_started (BLOCKED) | Actual business rules, requirements and owner |

### Queue C — reject duplicate trusted tenant claims (execution 6/10; 2026-11-13)

[PR #20](https://github.com/smael-aftersearch/ABAFramework/pull/20) ([exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/20/checks)) adds a technical, owner-independent fail-closed check in `AbaTenantClaims.Resolve`: even within one authenticated identity, two `tenant_id` values (different **or identical**) cannot authorize a tenant SQL scope by first-match selection. Focused xUnit tests cover the valid single-tenant happy path, two negative duplicate-claim tenant-policy cases and a quota-partition invariant denying spoofed tenant/header bucket variation. These tests are **AUTHORED; execution must be confirmed by exact-HEAD PR CI**, not inferred from the earlier successful [Queue C CI 43357853144](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43357853144).

Queue C statuses remain `api-shared-limits=partial`, `auth-membership=not_started`, `auth-sql-rls=partial`, `auth-raw-sql=partial`, `biz-contracts=partial`, `biz-contract-tests=not_started`, `biz-endpoints=not_started`. **Verified closures 0/7 (0%)**. Named owner/membership model, production DBA/Security-approved RLS and SQL session-spoof defenses, business V1 Contracts/endpoints and gateway/Redis approvals remain open or **BLOCKED**. No production change or new domain ownership is inferred.

### Queue C — reject ambiguous signed subject quota buckets (execution 7/10; 2026-11-13)

[PR #21](https://github.com/smael-aftersearch/ABAFramework/pull/21) ([exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/21/checks)) hardens `api-shared-limits` without inventing any business membership or tenant role: a validated principal with repeated `sub` or mapped subject identifiers now falls back to the actual TCP peer, not the first signed value. Local and opt-in Redis quotas use the same partition key. Authored xUnit tests cover the legitimate single-identifier happy path, malicious duplicate/reordered subjects and forged tenant/forwarded headers sharing one exhausted quota, and health/identifier-alias invariants. **Exact-head CI is pending; no new test is claimed PASS.** All statuses remain `api-shared-limits=partial`, `auth-membership=not_started`, `auth-sql-rls=partial`, `auth-raw-sql=partial`, `biz-contracts=partial`, `biz-contract-tests=not_started`, `biz-endpoints=not_started`; verified full closures **0/7 (0%)**. Production gateway/Redis certificate and ACL approvals, Identity owner/membership model, DBA/Security production RLS and business Contracts/endpoints are BLOCKED.

### Queue C — dual-stack anonymous peer quota normalization (execution 8/10; 2026-11-13)

`api-shared-limits` now canonicalizes IPv4-mapped IPv6 TCP peers to the same IPv4 quota identity. An unauthenticated peer cannot obtain separate local/Redis budgets by appearing as `192.0.2.25` versus `::ffff:192.0.2.25`; forged tenant/forwarded headers and unsigned subject claims remain irrelevant. Generated xUnit acceptance covers budget exhaustion across the two address forms (negative), a legitimate native IPv6 peer budget (positive), unambiguous JWT subject and health exemption (invariants). **New patch is AUTHORED / NOT RUN until exact-head CI.** The [previous main CI 43370628443](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43370628443) passed 10/10 jobs including NuGet and scanned OCI-archive signed source identity/tamper rejection on prior `4dadc6d0`, not the new HEAD. [PR #24 diff and exact-head CI checks](https://github.com/smael-aftersearch/ABAFramework/pull/24/checks) requires PR review and its own exact-head checks.

| Queue C feature ID | Status | Unresolved acceptance / blocker |
| --- | --- | --- |
| `api-shared-limits` | partial | Gateway trust, Redis server certificate/ACLs, deployment/load approval |
| `auth-membership` | not_started (BLOCKED) | Named Identity owner and real member/role model |
| `auth-sql-rls` | partial (BLOCKED for production) | Context owner/DBA-approved migration and least-privilege roles |
| `auth-raw-sql` | partial (BLOCKED for production) | Session-spoof defense, approved SQL usage and Security review |
| `biz-contracts` | partial (BLOCKED) | Owner-specified versioned business schemas |
| `biz-contract-tests` | not_started (BLOCKED) | Defined producers/consumers and contract assertions |
| `biz-endpoints` | not_started (BLOCKED) | Business requirements and authorization policy |

**Fully verified C capability closures: 0/7 (0%)**. Technical Platform code does not create real membership, new business Contracts, production RLS, secrets or deployments.

**Queue C fully closed: 0/7 (0%)**. Evidence: [Queue C #5](https://github.com/smael-aftersearch/ABAFramework/issues/5)
and [central #6](https://github.com/smael-aftersearch/ABAFramework/issues/6).
No new tests are described as PASS until their exact-commit workflow completes.

The table records *verified feature closures*, not speculative percentage-complete estimates. For exact feature IDs, acceptance criteria, owners and evidence, follow [the generated backlog](docs/IMPLEMENTATION-STATUS.md). After each substantive code change, update the README and source status JSON only to reflect actual tested changes, regenerate the status matrix, and record the PR/CI evidence in the relevant issue. Automated merge is allowed only after real tests, review and branch-policy gates. Business-owner approvals, production secrets, release policy, and deployment decisions remain outside the authority of these tasks.


**Queue A source verification (13 November 2026):** [main CI 43335432195](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43335432195) passed **9/9 jobs** at commit `8ba76cd8285f4101a70c35271f43d4e5a400e7a9`, including both signed NuGet and Docker-save OCI archive attestations and altered-artifact rejection. This closes the OCI token incident in [#3](https://github.com/smael-aftersearch/ABAFramework/issues/3), **not** the published-registry release gate: `dev-release-sbom` remains **partial**, with Queue A capability closures still **0/7 (0%)**. The next scoped change consolidates both verifier steps into [one fail-closed script](scripts/verify_attested_artifact.sh) and adds a deliberate wrong-source-SHA rejection with [positive/negative tests](scripts/test_release_attestation.py). These additional checks are not considered release-verified until the new PR checks and main-only provenance job pass.

**Queue A provenance (13 November 2026):** [main CI 43346615178](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43346615178) passed **9/9 jobs** at commit `2aae00bbb35ea6031640f5a510855cf4556a8fca`, including signed NuGet and scanned Docker-save OCI archive source-identity and mutation rejection. PRs [#7](https://github.com/smael-aftersearch/ABAFramework/pull/7) and [#9](https://github.com/smael-aftersearch/ABAFramework/pull/9) are merged. `dev-release-sbom` remains **partial**: published OCI registry signatures/release approvals are not proven; Queue A closures **0/7 (0%)**.

**Queue A run 4/10 — `otel-collector` + `otel-instrument` ([PR #12](https://github.com/smael-aftersearch/ABAFramework/pull/12), [verified PR-head CI 43348974903](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43348974903) and [main CI 43353408323](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43353408323)):** an opt-in, loopback-only genuine OpenTelemetry Collector is added to the real GitHub Actions gate; a fresh API must export attributed HTTP traces and metrics, reject collector execution without CI opt-in and not leak a synthetic credential. The disposable Collector export was actually verified on PR and main (not in production). At that historical checkpoint both IDs were partial; the current matrix marks `otel-collector` implemented for this CI integration, while `otel-instrument` remains partial pending correlation and production policy. Queue A's broader production closures remain **0/7 (0%)**. Evidence: [collector runner](scripts/verify-otel-collector.sh), [negative/invariant gate tests](scripts/test_otel_collector_gate.py), [CI workflow](.github/workflows/verify.yml), [Queue A ledger](https://github.com/smael-aftersearch/ABAFramework/issues/3).


### Queue A — bounded CI loopback baseline (`ops-load`, run 6/10)

Patch and review: [PR #15](https://github.com/smael-aftersearch/ABAFramework/pull/15); [first-head CI 43354991363](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43354991363) verifies the bounded probe only when its job is green. Every later HEAD must separately pass all PR checks; this link is not a shortcut around required approvals.

A new [CI-only load probe](scripts/ci_loopback_load.py) sends **40 liveness GETs** at at most **4 concurrent workers** to the disposable, generated API loopback listener, recording status failures, throughput, p50 and p95 in a downloadable artifact. [Negative/invariant tests](scripts/test_ci_loopback_load.py) reject non-loopback hosts, redirects, credentials, query strings, unbounded counts and execution without CI opt-in. The probe is a deliberately small single-process diagnostic, **not** a published performance SLO, provider failover test or production traffic authorization; the focused [PR-head Collector job](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43354991363) passed; entire PR-head CI and required approvals are separate gates.

| Queue A ID | Verified completion | Residual work |
| --- | --- | --- |
| `dev-release-sbom` | partial | Actual published OCI digest and NuGet release provenance/approval |
| `ops-audit` | partial | Immutable off-DB custody, keys and context policy |
| `ops-prod-secrets` | not_started | Owner-approved secret store and rotation |
| `dev-prod-deploy` | not_started | Approved target and rollback |
| `otel-instrument` | partial | Production PII/sampling/correlation |
| `otel-collector` | implemented (CI-only technical pipeline) | Production TLS/mTLS/export/retention |
| `ops-load` | partial (disposable CI probe plus private artifact guard) | Two-replica load/failover, SLO and capacity approval |

**Queue A run 7/10 — `ops-load` evidence integrity:** [PR #18](https://github.com/smael-aftersearch/ABAFramework/pull/18) confines its CI-only JSON output to a **new** `artifacts/load/<safe-name>.json` file, created exclusively at mode `0600`. Traversal, absolute destinations, pre-existing files and symlinks now fail before any HTTP request; the existing 40-request / four-worker liveness limits and opt-in remain unchanged. [Previously successful main CI 43356449614](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43356449614) proves the earlier probe and signed artifact gates; the new guard has **8/8 focused Python tests PASS locally**, while PR #18 exact-head CI is pending independent verification. `ops-load` remains **partial**, Queue A completed **0/7 (0%)**; no production SLO, deployment or secrets.

**Queue A run 9/10 — `otel-instrument` (technical OTLP URL guard, [PR #22](https://github.com/smael-aftersearch/ABAFramework/pull/22), [exact-head CI and review](https://github.com/smael-aftersearch/ABAFramework/pull/22/checks), pending required gates):** the Platform configuration now rejects exporter URLs containing inline credentials, query parameters or fragments *before* OpenTelemetry wiring, in both Development and Production. New generated API xUnit happy-path/negative/invariant cases preserve safe HTTPS paths, existing Development-only HTTP and opt-out behavior. This does not supply production collector credentials, mTLS, sampling, PII retention, or an approved destination. [Latest prior main-only CI 43367344522](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43367344522) passed all 10 jobs with NuGet/OCI archive signing and tamper rejection at `4147122b5fe11158073c890a78032e8499a66742`; the proposed patch requires its **own exact-head CI and review** before any merge. All seven ID states in the table above remain unchanged; fully closed capabilities **0/7 (0%)**.

**Queue A run 10/10 — `ops-load` canonical loopback target guard ([PR #25](https://github.com/smael-aftersearch/ABAFramework/pull/25), [exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/25/checks)):** A scoped technical fix rejects raw URL control characters and noncanonical spellings that Python's `urlsplit` can silently normalize into `127.0.0.1` before any HTTP traffic. Positive canonical URL, negative CR/LF/TAB/ambiguous variants, no-request invariant and existing 40/4 limits are covered by the same executable Python suite. [Entry-main CI 43372751506](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43372751506) passed 10/10 jobs with main-only NuGet/OCI archive source verification and tamper rejection at `7f7a43ec333eb21ddc159850f37a0a593b58ca7b`; the newly authored patch must pass its own exact-head CI and review before merge. This final technical patch does not establish a production SLO, rollout or owner-approved load policy. All seven ID statuses in the table remain unchanged and completions remain **0/7 (0%)**.

**Fully closed Queue A features: 0/7 (0%)**. Latest previously verified `main` signed NuGet and scanned OCI-archive [CI 43353408323](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43353408323) at `21ae453072bc634ba10dabedc4d13179232c116a`; the new `ops-load` probe passed focused PR-head Collector checks; full PR gates and any future HEAD must independently pass. [Queue A ledger](https://github.com/smael-aftersearch/ABAFramework/issues/3) · [central ledger](https://github.com/smael-aftersearch/ABAFramework/issues/6).

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

## Host-wide and opt-in cross-replica cache-miss coordination and reviewed OCI supply chain

The generic API Platform now coalesces concurrent authorized
read-through misses for the SAME scoped key within one process;
different tenants/keys do not block one another. Default(T) and JSON
null cache hits are distinguished from misses. A separately provisioned
Redis CI job actually stops/restarts its disposable Redis service and
proves strict failure, explicit fresh-source fallback and reconnect.
The default miss gate remains host-local. Optional Redis coordination adds
shared leases, a concurrent-source ceiling and a fixed-window source budget;
it refuses source fallback without a live shared permit. Neither mode is an
authorization control or business correctness lock.

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

**Implemented technical integrations (opt-in):** EF Core 10 for **both PostgreSQL and Microsoft SQL Server**, provider-neutral migrations, relational idempotency receipts, Redis distributed cache with TTL and scope isolation, Wolverine 6 durable local messaging with EF transactional middleware for either SQL engine, and authored opt-in RabbitMQ/Kafka transports, plus OpenTelemetry traces/metrics with optional OTLP export. Optional authenticated JWT tenant-claim enforcement and context-owned EF Core query/write guards are implemented; real tests on **both** database engines verify denial of cross-tenant EF reads and writes. This does **not** provide SQL-level RLS, trusted tenant membership or protection against raw SQL/bulk operations. External brokers remain disabled by default; live RabbitMQ/Kafka cases still need exact-head CI execution.

**Current evidence:** [full generic-framework run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43313417455) verifies 16 real-service scenarios (Wolverine transactional outbox and clean restart on both engines, transactional idempotency, EF tenant protection on both SQL engines, Redis), plus actual Keycloak/OIDC tests, Docker startup, xUnit and template packaging. The module generator creates Domain/Application/Infrastructure/Contracts, owner metadata and tests; `--with-db` adds an optional context-owned EF shell for either SQL engine. **Not certified:** RabbitMQ outage recovery until the new isolated broker tests run, ungraceful process-kill recovery, poison/dead-letter replay, live tenant membership and production RLS rollout, migration rollback/backup plans, load/security audit or production deployment.

Setup and connection settings: [generic platform infrastructure guide](templates/aba-api/docs/platform-infrastructure.md). Local optional services: `docker compose -f infra/compose.platform.yaml up -d`. Remaining quality gates: `docs/NEXT-MILESTONES.md`.

---

## Implemented versus not yet implemented

**Queue C — execution 9/10, `auth-raw-sql` open-failure cleanup (2026-11-13):** A domain-neutral tenant SQL scope now closes a disposable provider connection even if `OpenAsync` partially opens it and then fails, without invoking the query callback. New happy/negative/invariant coverage proves that a valid scoped tenant reaches the synthetic open boundary, malformed or missing tenant claims never open a connection, and a failed open leaves no reusable connection. Existing real PostgreSQL/SQL Server tenant-owned-row success and cross-tenant raw-SQL/EF-bulk rejection suites remain mandatory. This change is **AUTHORED, pending exact-head CI; not production tenant authorization**. Feature states are `api-shared-limits=partial`, `auth-membership=not_started`, `auth-sql-rls=partial`, `auth-raw-sql=partial`, `biz-contracts=partial`, `biz-contract-tests=not_started`, `biz-endpoints=not_started`; **verified Queue C closures: 0/7 (0%)**. Business Owner/DBA/Security approvals, product memberships, roles, RLS migrations and business contracts/endpoints remain **BLOCKED**. [Queue C #5](https://github.com/smael-aftersearch/ABAFramework/issues/5), [central #6](https://github.com/smael-aftersearch/ABAFramework/issues/6), [PR #27 review and exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/27/checks).

**Queue C — execution 10/10 final technical patch (2026-11-13):** `auth-raw-sql` refuses a pre-cancelled request **before** calling the SQL provider's `OpenAsync`, so even a non-compliant provider cannot acquire a tenant connection after cancellation. The disposable test asserts an `OperationCanceledException` and zero provider opens, closes or tenant callbacks (negative/no-side-effect invariant); the existing valid-tenant real SQL integration and missing/malformed-tenant tests remain the positive and bypass denial gates. Source and tests are AUTHORED; count them PASS only when [PR #28 exact-head checks](https://github.com/smael-aftersearch/ABAFramework/pull/28/checks) succeed. Prior [PR #27](https://github.com/smael-aftersearch/ABAFramework/pull/27) was merged; [main CI 43380126720](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43380126720) passed 10/10 jobs at `2467d600134d` including signed NuGet and scanned OCI archive source/tamper checks. This new patch is not production approval.

| Queue C ID | Current status | Remaining blocker |
| --- | --- | --- |
| `api-shared-limits` | partial | Gateway trust, Redis ACL/TLS identity, production-load approvals |
| `auth-membership` | not_started — BLOCKED | Actual owner-approved Identity membership, role and permission model |
| `auth-sql-rls` | partial — BLOCKED for rollout | DBA/Security and owner-approved migrations, restricted DB roles |
| `auth-raw-sql` | partial | Tenant session-spoof/SQL injection threat model and approved SQL caller policy |
| `biz-contracts` | partial — BLOCKED | Owner-defined real versioned business contract schemas |
| `biz-contract-tests` | not_started — BLOCKED | Actual producer/consumer contracts and compatibility requirements |
| `biz-endpoints` | not_started — BLOCKED | Product requirements, business decision owner and security policy |

| Technical work in this cycle | Evidence | Capability closed? |
| --- | --- | --- |
| Validated tenant-claim/quota identity and dual-stack peer guards | [PR #17](https://github.com/smael-aftersearch/ABAFramework/pull/17), [PR #24](https://github.com/smael-aftersearch/ABAFramework/pull/24) | No — partial |
| Partial-open tenant SQL cleanup | [PR #27](https://github.com/smael-aftersearch/ABAFramework/pull/27), [main CI](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43380126720) | No — partial |
| Reject pre-cancelled tenant SQL provider access | [PR #28](https://github.com/smael-aftersearch/ABAFramework/pull/28/checks) — exact-head verification required | No — partial |

**Fully closed Queue C capabilities: 0/7 (0%)**, unchanged until externally owned production and business acceptance gates pass. [Queue C issue #5](https://github.com/smael-aftersearch/ABAFramework/issues/5) · [central ledger #6](https://github.com/smael-aftersearch/ABAFramework/issues/6).

**Queue B — execution 9/10, `cache-stampede` (2026-11-13):** The non-authoritative read-through query cache permits a **deliberately opted-in** authoritative reload and write-back repair when a scoped cache entry contains malformed JSON. Strict cache policy still propagates the JSON error without loading, pre-cancelled requests cannot load or overwrite, and a neighbor tenant's same-named query key stays untouched. The existing host-local single-flight and source budget still bound callers. The positive, negative and tenant-isolation tests are authored, with [PR #29 exact-head checks](https://github.com/smael-aftersearch/ABAFramework/pull/29/checks) required for PASS. [Pre-change main CI 43382204455](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43382204455) was PASS 10/10 at `55534421dcbc` **only**. No owner-specific business contracts, production cache rollout or distributed lock was claimed at that time.

**Post-queue technical addition — opt-in Redis cache coordination (2026-11-13):** The generic Platform now has an opt-in token lease per hashed cache key, a Redis-shared concurrent source ceiling and a fixed-window source-load budget. Separate service providers share misses and budgets; if Redis cannot grant a lease or budget, no source fallback runs. API xUnit passed locally **190/190**; the integration project builds and the unreachable-Redis fail-closed case passes. Multi-host Redis integration cases are authored but **NOT RUN** locally because Docker Engine is stopped. This is still `cache-stampede=partial`: live disposable Redis verification, callback cancellation under lease loss, business query-scope authorization and Production Redis topology review remain open.

| Queue B ID | Verified capability status | Remaining gate |
| --- | --- | --- |
| `db-business-migrations` | not_started — BLOCKED | Named business owner and approved schema/migrations |
| `db-concurrency` | not_started — BLOCKED | Approved aggregate conflict semantics |
| `db-backup-restore` | partial | Production recovery/PITR and DBA restore approval |
| `msg-poison-replay` | partial | Trusted operator identity, tenant-store ACL and replay policy |
| `msg-crash-window` | partial | Actual business Outbox/ACK fault windows and external dedup |
| `msg-broker` | partial | Exact-head duplicate delivery and isolated broker recovery evidence; approved production topology and policies |
| `msg-domain-consumers` | not_started — BLOCKED | Owner-defined contracts and real consumers |
| `cache-multinode` | not_started | Cross-replica invalidation with tenant permissions |
| `cache-stampede` | partial | Multi-replica miss budgets and real outage-load evidence |

| Technical change | Evidence | Capability fully closed? |
| --- | --- | --- |
| Corrupt scoped JSON query cache: explicit repair, strict denial, tenant isolation | [PR #29](https://github.com/smael-aftersearch/ABAFramework/pull/29), [exact-head CI](https://github.com/smael-aftersearch/ABAFramework/pull/29/checks) | No — partial |
| Bounded cache-miss registrations and Redis readiness | [PR #13](https://github.com/smael-aftersearch/ABAFramework/pull/13), [PR #23](https://github.com/smael-aftersearch/ABAFramework/pull/23) | No — partial |
| Wolverine SIGKILL fixture safety and poison replay identity | [PR #26](https://github.com/smael-aftersearch/ABAFramework/pull/26), [PR #19](https://github.com/smael-aftersearch/ABAFramework/pull/19) | No — partial |

**Fully closed Queue B capabilities: 0/9 (0%).** Business owners/DBA/Operations still own production requirements and approval. [Queue B #4](https://github.com/smael-aftersearch/ABAFramework/issues/4) · [central ledger #6](https://github.com/smael-aftersearch/ABAFramework/issues/6).

### Post-PR #31 technical hardening (2026-11-13)

[PR #31](https://github.com/smael-aftersearch/ABAFramework/pull/31) was
merged at `fddc7953bf746d74b6ef974d7818453d04e281d0` after
[exact-head CI 43495491046](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43495491046)
passed all 10 applicable jobs, including the disposable RabbitMQ outage /
producer-SIGKILL / committed transactional outbox recovery. The **current**
catalog has 55 capabilities: **36 implemented, 11 partial, 8 not started**.
These are technical status categories, **not** Production authorization.

**`cache-stampede` incremental Redis fail-closed drift guard:**
[PR #33](https://github.com/smael-aftersearch/ABAFramework/pull/33)
revalidates opt-in cross-host namespace settings before each Redis
coordination operation instead of permanently trusting the first check.
A disposable Redis regression exercises same-settings recreation, conflicting
replacement (no source loader runs), unhealthy readiness and preservation
of two tenants' cached results. [Exact-head CI 43498016241](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43498016241)
passed **10/10 applicable jobs**, including the new real Redis integration
regression; [PR #33](https://github.com/smael-aftersearch/ABAFramework/pull/33)
was squash-merged as `0eef067738d23231e09435ecb3415576bff545ce`.
The feature remains **partial**, while `cache-multinode` remains **not started**; no
business invalidation protocol or Production failover/eviction policy is
implied. See [next-phase issue #32](https://github.com/smael-aftersearch/ABAFramework/issues/32).

**Further real Redis lease-loss safety (incremental):** an independent
disposable two-replica regression now revokes only a generated hashed
cache-miss lease while an intentionally cancellation-blind source is running.
It requires the first replica to reject its late stale value, the new
replica's authorized query value to persist, and another tenant's data to
remain unchanged. The renewer also stops extending a source slot after
its per-key lease is lost. Source/test/ADR are authored in a separate
small change; **exact-HEAD CI is required before calling this PASS**.
No Production failover or business tenant scope is certified, and
`cache-stampede` remains `partial`.

<!-- ABA-STATUS:START -->

The **generic .NET 10 API template** is the primary product. This matrix is separate from the
historical Academy Phase 1 demo below. A feature can be implemented technically while
its **production release gates remain open**. Evidence refers to the last reviewed baseline:
[CI run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289)
(commit `dd77a1c1b0f2`). New commits must pass their own CI before being treated as verified.

**Tracked: 55** · Implemented: 36 · Partial: 11 · Not implemented: 8.

| Area | Parameter / feature | Status | Delivered scope | Remaining task | Priority |
| --- | --- | --- | --- | --- | --- |
| API | Domain-neutral .NET 10 HTTP API | **Implemented** | Versioned Minimal API with composition root | No generic framework work outstanding | P0 |
| API | OpenAPI plus interactive Scalar | **Implemented** | Generated OpenAPI and Development-only Scalar UI | Production API docs need a reviewed access policy | P1 |
| API | Problem Details and HTTP errors | **Implemented** | RFC 7807 HTTP 500 with nonsecret Production response, validated header/body correlation ID and focused HTTP failure tests | Review validation/client errors, proxy traces and application logging policy per deployment | P1 |
| API | Request correlation IDs | **Implemented** | Validated X-Correlation-ID response header | Correlate logs, traces and message envelopes across services | P1 |
| API | Liveness and readiness | **Implemented** | Independent liveness and ready-tagged dependencies; injected health failure returns live 200 and ready 503 | Prove real database/Redis outage, reconnection and readiness behavior under load | P0 |
| API | Authenticated per-identity rate limits | **Implemented** | JWT subject + tenant partition; anonymous TCP peer; Health bypass | Multi-node shared quotas require a gateway | P1 |
| API | Configurable browser CORS allow-list | **Implemented** | Exact configured origins only; HTTPS outside Development, loopback HTTP allowed only in Development, constrained methods/headers and no credential cookies; 11 focused xUnit cases pass; exact PR #31 CI run 43413084747 passed all 9 applicable jobs | Each consuming deployment must provide its reviewed application origin list | P1 |
| API | Distributed multi-instance rate limits | **Implemented** | Opt-in atomic Redis quota passes 12 concurrent requests across two independent HTTP replicas (3 allowed, 9 denied), forged tenant/forwarded headers cannot reset quota, namespaces remain isolated, Redis outage fails closed, and separate OS-process quota probe passes; exact main CI 43393740493 at 6a589a8 | Production proxy trust, Redis TLS/ACLs, monitoring and deployed-replica load controls require Operations approval; disposable probes do not certify a production topology | P1 |
| Security | OIDC/JWT signature, issuer and audience | **Implemented** | ASP.NET JWT bearer validation and fail-closed production OIDC config | External real-user IdP configuration and security operations remain | P0 |
| Security | Real Keycloak OIDC/JWKS test | **Implemented** | Ephemeral Keycloak CI verifies signed 204, missing/malformed 401, tenant 403 | Real IdP key rotation and organization policy not validated | P0 |
| Security | Trusted tenant claim required when enabled | **Implemented** | JWT tenant claim fallback/default authorization; ignores raw tenant header | Prove organization membership in actual IdP | P0 |
| Security | Tenant membership / RBAC / ABAC | **Not implemented** | Not implemented | Define owning membership model, roles and permissions | P0 |
| Security | EF tenant row query and SaveChanges guards | **Implemented** | Tenant-scoped EF filtering and foreign-row write rejection; TenantDatabaseBoundaryTests passed 2/2 against both live SQL providers. | Raw SQL, bulk updates and SQL-level RLS remain open | P0 |
| Security | Database-enforced Row-Level Security | **Implemented** | Technical acceptance passes on both SQL engines in PR #31 head CI 43417678561: PostgreSQL FORCE RLS and SQL Server FILTER/BLOCK DDL, trusted tenant scopes, and direct SQL + EF bulk cross-tenant denial; RowLevelSecurityTests passed 2/2 against both live SQL providers. | Apply owner-specific RLS migrations and least-privilege roles, verify membership authorization and privileged/session spoof bypass cases | P0 |
| Security | Bulk/raw SQL tenant authorization | **Partial** | CI-verified tenant-scoped SQL transaction wrapper with cross-tenant raw SQL/EF bulk denial on both SQL engines; split-identity tenant guard verified in CI 43357853144; duplicate tenant-claim authorization denial and quota invariants (PR #20 CI 43364852219 PASS); Queue C technical SQL scope closes partially opened connections after OpenAsync failure and refuses pre-cancelled provider access; exact-head CI 43471120522 passed the RLS raw SQL/EF bulk denial on PostgreSQL and SQL Server and the current generated API xUnit suite. Local TenantRlsPolicyTests 16/16 reject SQL tautology, PostgreSQL set_config and SQL Server SESSION_CONTEXT payloads before opening a connection. | Prevent arbitrary SQL callbacks from changing tenant session context via direct set_config/sp_set_session_context calls; adopt real owner-context raw SQL authorization, least-privileged production accounts and DBA/Security approvals. | P0 |
| Persistence | Production SQL TLS and certificate identity | **Implemented** | Production startup rejects PostgreSQL without VerifyFull and SQL Server without verified encryption; real container negative tests passed | Configure real certificate authority/trust chain and rotation in target deployment | P0 |
| Persistence | EF Core 10 PostgreSQL provider | **Implemented** | Runtime Npgsql provider selected by config; Local PostgreSQL 17 SQL integration checks passed; see the exact run report. | Context-owned schema migrations require real domains | P0 |
| Persistence | EF Core 10 Microsoft SQL Server provider | **Implemented** | Runtime EF SqlServer provider selected by config; Local SQL Server 17.0.1000.7 SQL integration checks passed; see the exact run report. | Context-owned schema migrations require real domains | P0 |
| Persistence | EF technical idempotency migration | **Implemented** | Provider-neutral checked-in EF technical migration; All 10 base migration/idempotency/durable-outbox cases passed across PostgreSQL 17 and SQL Server 17.0.1000.7. | Production migration rollback/backup remains unverified | P0 |
| Persistence | Optional per-context EF Core scaffold | **Implemented** | --with-db creates an empty context-owned DbContext for both SQL engines and now authors provider-aware EF design-time tooling from ABA_DB_PROVIDER / ABA_DB_CONNECTION; no business entities or migrations are invented | Actual business entities, migrations and policies still require the named context owner | P1 |
| Persistence | Business context schema and migrations | **Not implemented** | Not implemented | Implement real owner-specific models and migrations after requirements | P0 |
| Persistence | Business optimistic concurrency handling | **Not implemented** | Not implemented | Implement rowversion/concurrency tokens and conflict semantics per context | P1 |
| Persistence | Backup, recovery, rollback and restore drill | **Implemented** | Opt-in disposable-database drill uses PostgreSQL 16 pg_dump/pg_restore and SQL Server checksum BACKUP/RESTORE VERIFYONLY/RESTORE; 7 provider and safety tests pass in PR #31 head CI 43417678561, with 6 local isolation guards passing on current test source | Prove business-schema and point-in-time recovery, migration rollback, offsite encrypted backups, RTO/RPO and operator restore rehearsal | P0 |
| Reliability | Transactional idempotency receipt and work | **Implemented** | Same-DbContext work and receipt commit/rollback together; Same-DbContext idempotency rollback/commit integration passed for both providers. | External provider side effects still require dedicated idempotency | P0 |
| Reliability | Concurrent duplicate request suppression | **Implemented** | Four concurrent callers; only one same-key transaction commits; Concurrent same-key idempotency integration passed for both providers. | Cross-service external payment retries need real owning context | P0 |
| Messaging | Wolverine 6 durable local queues | **Implemented** | Durable relational messaging with either SQL engine; Durable local Wolverine delivery passed against both live SQL providers. | External broker delivery not yet wired | P0 |
| Messaging | EF Core/Wolverine technical transactional Outbox | **Implemented** | Technical state+outgoing message atomic under precommit fault; Transactional EF state plus Wolverine outbox commit/rollback passed for both providers. | Real business consumers and crash windows remain open | P0 |
| Messaging | Durable scheduled message across clean restart | **Implemented** | Persisted scheduled Wolverine message recovers after host restart; Durable scheduled-message clean restart recovery passed against both providers. | Abrupt crash and poison replay remain unverified | P1 |
| Messaging | Poison/dead-letter inspection and replay | **Partial** | CI-verified opt-in Wolverine poison quarantine, metadata-only inspection and exact-ID audited replay on both SQL providers; single-authenticated-identity binding verified in PR #16 CI 43356676386; exact-one operator sub claim ambiguity denial and dual-provider replay regressions passed in exact-head CI 43472910020. A persisted five-minute one-use inspection receipt bound to envelope/types/operator/ticket now passes local generated-template unit tests; dual-provider integration remains NOT_RUN locally because Docker is unavailable. | Issue real signed operator IdP permission, tenant-scoped message store authorization, business replay idempotency/compensation, volume and receipt-retention policy, and approved production runbook | P0 |
| Messaging | Crash-kill Outbox recovery and duplicate delivery | **Partial** | CI-verified real SIGKILL on both PostgreSQL and SQL Server: uncommitted SQL rollback, durable Wolverine schedule recovery and genuine pre-ACK redelivery after one separately committed idempotent technical effect (8/8 CrashDrill cases). The additional disposable PostgreSQL/RabbitMQ crash/recovery drill is authored and the integration project builds locally with zero warnings; live service execution is NOT_RUN locally because Docker is unavailable. Strict crash-marker guards passed in exact-head CI 43471120522. | Owner-specific transactional business Outbox COMMIT/publish/consumer ACK fault tests, external provider side-effect deduplication, broker partitions, multi-node fencing and Production recovery approvals | P0 |
| Messaging | Optional external RabbitMQ/Kafka transport | **Partial** | Opt-in Wolverine RabbitMQ with tracked publisher confirms plus opt-in Wolverine Kafka; owner-supplied versioned routes; Development-only topology provisioning; RabbitMQ AMQP TLS and Kafka SASL_SSL production guards; Kafka idempotent producer with acks=all; connection/secret redaction. Production RabbitMQ also rejects the built-in guest username case-insensitively even with a custom password; the regression passes in local API xUnit (215/215). Integration project Release build passes with zero warnings. Exact-head CI 43472910020 passed RabbitMQ duplicate delivery on PostgreSQL and SQL Server, Kafka duplicate delivery on both providers (4/4) and disposable RabbitMQ outage/recovery. Local live broker cases are NOT_RUN because Docker Desktop is unavailable. | Review owner-approved Production topology/ACLs, broker membership, retry/dead-letter policy, schema/partition policy and business consumers. Generic full verification outside the dedicated GitHub Actions service job reports NOT_RUN for external services. | P1 |
| Messaging | Real context-owned events and consumers | **Not implemented** | Not implemented | Implement owner-approved domain contracts, Inbox and cross-context handlers | P0 |
| Caching | Redis distributed cache with TTL and scopes | **Implemented** | StackExchangeRedis adapter with scoped JSON TTL/invalidation | Distributed invalidation and PII audit remain open | P1 |
| Caching | Read-through fallback when Redis unavailable | **Implemented** | Default fail-closed; explicit fresh authoritative query fallback | Cluster reconnect, authorized workloads and load behavior unverified | P1 |
| Caching | Cross-instance invalidation and tenant permissions | **Not implemented** | Not implemented | Add versioned invalidation events with authorized namespace boundaries | P1 |
| Caching | Cache stampede and Redis restart resilience | **Partial** | Host-wide per-tenant/key single-flight read-through with 24-concurrent-call and real 12-caller Redis tests, TTL/cancellation/miss-value fixes; actual CI Redis stop/restart with strict/explicit fallback and reconnect; CI-verified per-host concurrency ceiling, capped in-flight registrations, readiness cleanup and cancellation guards; opt-in distributed per-key lease, cross-host source slots, fixed-window budget, settings-drift guard and fail-closed readiness. The cancellation-blind loader lease-loss regression passes local and CI API xUnit. Exact-head CI 43471120522 passed cross-host same-key collapse, shared source/rate budgets, namespace drift and coordinator-outage cases. Live Redis cases are NOT_RUN locally because Docker Engine is unavailable. | Review Redis failover, lease expiry, eviction and production capacity/TTL policies; owning contexts must authorize each query scope. Platform discards late results but cannot undo side effects from a loader that ignores cancellation. | P1 |
| Observability | OpenTelemetry ASP.NET/HTTP traces and metrics | **Partial** | Optional tracing/metrics instrumentation and OTLP exporter wiring; endpoint URL validation refuses embedded credentials, queries and fragments before exporter configuration; low-cardinality cache read outcome and source-load count/duration metrics expose no tenant, scope, key or exception dimensions, with local API xUnit regression tests passing (215/215). Exact-head CI 43472910020 passed the prior generated API OTLP/gRPC collector trace and metrics probe. | Approve production PII/sampling/correlation policy and prove the deployed collector destination, trust and retention behavior; CI proves only the generated-API disposable collector path. | P1 |
| Observability | Collector pipeline and export integration test | **Implemented** | Technical acceptance passes on exact PR #31 head CI 43415885594: isolated OTLP/gRPC Collector receives generated API spans and HTTP metrics with the correct service name; loopback-only receiver, synthetic sensitive-header non-leak and five fail-closed gate tests | Approve production collector routing, HTTPS/mTLS, PII controls, sampling and retention; no published telemetry destination | P1 |
| Observability | Tamper-evident audit trail | **Partial** | CI-verified HMAC-SHA256 per-tenant ledger, serializable dual SQL append, tamper/rotation tests plus separately signed offline checkpoint codec and historical-prefix verification on both SQL engines; null-digest verification/signing negatives pass locally and in the generated API suite. Exact-head CI 43471120522 passed ledger append/tamper/rotation and anchored checkpoint tests on PostgreSQL and SQL Server. External immutable archive and key custody remain open. | Deploy independently immutable off-DB checkpoint archive, real signing key custody/rotation and context-owned actor/event/retention rules; audit appender role restrictions and legal policies | P0 |
| Operations | Production secrets, key rotation and policy | **Partial** | Root and generated-template CI now use a source/reachable-history Gitleaks scan pinned by immutable OCI digest; the isolated container has no network/write access and scanner output is suppressed. Synthetic-token positive/negative unit tests pass (4 tests, 2 Docker-backed fixture checks skipped); the actual local source/history scan is NOT_RUN because Docker is unavailable, and CI fails closed when its scanner cannot run. | Select and implement the approved production secret store, workload identity, rotation, least-privilege DB/cache/broker accounts, and verify secrets are absent from built images and operational logs | P0 |
| Developer | Installable versioned NuGet API template | **Implemented** | Installable versioned v0.3.0-preview.2 NuGet API template built, installed and tested with 73 xUnit cases in CI | NuGet.org publication, provenance/signing and release automation remain gated | P1 |
| Developer | Domain/Application/Contracts/Infrastructure generator | **Implemented** | Scaffold real named contexts with ownership docs; no fictional entities | Complete real domain behavior remains owner-dependent | P1 |
| Governance | ABA ownership and forbidden-edge checks | **Implemented** | Repo and generated-project architecture checks with negative cases | Semantic/IL enforcement still requires human architecture review | P0 |
| Governance | Portable agent Skill, task plan and verification | **Implemented** | Root and generated-project agent workflows provide task-aware reading/test plans and PASS/FAIL/NOT_RUN evidence | AI agent adoption varies by platform; semantic review still manual | P1 |
| Governance | Machine-readable feature status and remaining tasks | **Implemented** | 55-feature machine-readable catalog drives README and detailed backlog; six negative/positive tests and CI drift gate passed | Maintain each feature's owner, test evidence, acceptance gate and backlog after future tasks | P1 |
| Testing | Default xUnit HTTP/authorization/EF tests | **Implemented** | 60+ API xUnit checks in fresh generated solution | Business invariants and load testing need real domains | P0 |
| Testing | Real PostgreSQL/SQL Server/Redis/Wolverine CI | **Implemented** | 16 executable real-service tests across both SQL engines | Crash, high-load and external broker remain release gates | P0 |
| Testing | CI bundled into each generated API | **Implemented** | Push/PR build gate and manually triggered real-service workflow | Generated-project pipelines need repository policy/hardening to be adopted | P1 |
| Deployment | Non-root OCI image and local Compose | **Implemented** | Multi-stage .NET 10 Dockerfile, localhost-only Development Compose | Production image signing, secrets/TLS/hosting still open | P1 |
| Deployment | SBOM, vulnerability scans and signed releases | **Partial** | NuGet direct/transitive advisory scans + SPDX; reviewed immutable .NET 10 multiarch SDK/runtime base digests with fail-closed policy/negative tests; Trivy-scanned runtime Docker-save archive keyless SLSA attested with Docker reload ID/tamper rejection from prior verified CI; signed tested NuGet template provenance; Dockerfile USER validation requires exactly one active USER $APP_UID in runtime and none in other stages. Local OCI lock tests pass 11/11, and exact-head CI 43471120522 passed supply-chain and container security jobs. | Approve HIGH/unfixed image severity policy, sign and verify the actual published OCI registry digest and published NuGet package, protected tag promotion and production deployment/change-control | P0 |
| Deployment | Production deployment with TLS, identity and rollback | **Not implemented** | Not implemented | Choose target hosting, configure DNS/TLS/secrets and deployment rollback | P0 |
| Operations | Latency/throughput/load and failover baseline | **Partial** | CI-verified disposable generated-API loopback drill (main CI 43372751506): 40 HTTP 200, four workers, p50/p95 and throughput; PR #18 guards exclusive private JSON output (CI 43358127202 PASS); strict canonical loopback URL parsing and control-character rejection pass locally (8 passed; Windows symlink case skipped without account privilege) and in exact-head CI 43471120522. | Set owner-approved production SLO/RTO/RPO and exercise actual provider-backed two-replica business load/failover, recovery and safe capacity thresholds; loopback liveness does not certify those | P1 |
| Business | Owner-defined versioned cross-context contracts | **Partial** | Context generator creates Contracts projects and validates version suffixes, ownership declarations, and published/consumed contract metadata shape; focused local tests pass 13/13 and fresh-context generation/build checks pass in verify-starter.sh and exact-head CI 43471120522. No real product contracts or business schemas were invented. | Specify real public V1 schemas, consumers and compatibility evolution | P0 |
| Business | Consumer-driven contract compatibility tests | **Not implemented** | Not implemented | Introduce consumer-driven tests for real event/HTTP contracts | P0 |
| Business | Actual product CRUD/workflows and invariants | **Not implemented** | Not implemented | Implement real domain entities/endpoints only from approved requirements | P0 |

### Release-critical tasks still open (P0)

| Task ID | Task / acceptance gate | Current state |
| --- | --- | --- |
| `auth-membership` | Define owning membership model, roles and permissions — Unauthorized member and cross-organization privilege escalation tests | Not implemented |
| `auth-raw-sql` | Prevent arbitrary SQL callbacks from changing tenant session context via direct set_config/sp_set_session_context calls; adopt real owner-context raw SQL authorization, least-privileged production accounts and DBA/Security approvals. — Cross-tenant raw SQL and bulk mutation are denied | Partial |
| `db-business-migrations` | Implement real owner-specific models and migrations after requirements — Both SQL engines perform context migration without shared business tables | Not implemented |
| `msg-poison-replay` | Issue real signed operator IdP permission, tenant-scoped message store authorization, business replay idempotency/compensation, volume and receipt-retention policy, and approved production runbook — Poison message is quarantined, inspected and safely replayed exactly as intended | Partial |
| `msg-crash-window` | Owner-specific transactional business Outbox COMMIT/publish/consumer ACK fault tests, external provider side-effect deduplication, broker partitions, multi-node fencing and Production recovery approvals — No message loss across abrupt process death and controlled duplicate deliveries | Partial |
| `msg-domain-consumers` | Implement owner-approved domain contracts, Inbox and cross-context handlers — Consumer cannot mutate another context's tables; duplicate events are safe | Not implemented |
| `ops-audit` | Deploy independently immutable off-DB checkpoint archive, real signing key custody/rotation and context-owned actor/event/retention rules; audit appender role restrictions and legal policies — Audit cannot be silently rewritten and records actor, action, tenant, outcome | Partial |
| `ops-prod-secrets` | Select and implement the approved production secret store, workload identity, rotation, least-privilege DB/cache/broker accounts, and verify secrets are absent from built images and operational logs — No plaintext production secret in repo/container/CI logs | Partial |
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
