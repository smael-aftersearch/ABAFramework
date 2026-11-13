# Next implementation milestones (2026-11-13)

The authoritative, maintained **55-feature status table** and prioritized open-task backlog live at [docs/IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md); the machine-readable source is [docs/implementation-status.json](implementation-status.json). This file is a high-level release narrative, not an independent status source. Run `python3 scripts/status_matrix.py --write` after every change.

## Executable task breakdown for the remaining 19 capabilities

This work queue is derived from the current `docs/implementation-status.json`: **11 Partial and 8 Not implemented** after adding the source/history secret-scan slice. Capability state remains owned by that matrix; this section only breaks the remaining acceptance work into reviewable tasks. Test cases are specified now and authored alongside code, but test/build execution is deliberately deferred until the implementation wave is complete, as requested.

**Scope decision confirmed by the user:** keep `templates/aba-api` generic and domain-neutral. Do not add Academy or invented business models/contracts/endpoints to the starter. Business-owner tasks below are ready for a future named product/context owner; they are not authorization to create those domains in the template.

### In progress in parallel

1. **`msg-poison-replay` — enforce inspect-before-replay and close technical replay gaps** (Partial; Platform / Operations). Source review found that `RequestReplayAsync` verified caller-supplied types but did not prove a prior inspection by the same operator. The Platform fix is now authored: a persisted five-minute one-use receipt binds envelope ID, observed types, authenticated subject and change ticket; only its SHA-256 hash is stored and it is atomically consumed. A fresh inspection may replace a consumed receipt after the bounded window only if Wolverine confirms the envelope remains non-replayable. Production IdP entitlements, tenant store authorization, business compensation and receipt-retention/volume policy remain owner decisions. Acceptance: receipt-backed authorized replay works once; missing/expired/mismatched/used receipts do not mark an envelope replayable; concurrent duplicate use yields at most one replay request and audit remains fail-closed. Tests are authored for normal inspect-then-replay, invalid/expired/wrong-actor/ticket/changed-envelope denial, interruption recovery and one-time concurrent consumption; execution is deferred.
2. **`otel-instrument` — add safe, low-cardinality cache metrics** (Partial; Platform). A subagent authored read outcome and source-load count/duration instruments with bounded outcome tags only; tenant, user, key, and exception values are not dimensions. Production PII/sampling, collector destination, trust, and retention decisions remain open. Acceptance: cache hit/miss and source load signals emit when instrumentation is enabled; disabled/unsafe exporter configuration does not send traffic; no sensitive or high-cardinality dimensions appear. Tests are authored; execution is deferred.
3. **`cache-multinode` — prove shared-store invalidation and decide whether a backplane is needed** (Not implemented; Platform + business owners). Source review confirmed that exact-key deletion via `IDistributedCache.RemoveAsync` is immediately visible to replicas sharing the same Redis database and compatible instance name; the missing capability is owner-approved invalidation semantics across separate stores/namespaces. A subagent authored a two-provider Redis integration case for shared-store removal and tenant-scope isolation. After that evidence, the consuming context and Identity/Security must decide whether exact-key shared-store deletion is sufficient or a versioned event/backplane is required. No public invalidation endpoint or event schema will be invented. Acceptance: removal by replica A causes replica B to miss; another scope's same-named key remains; any approved event later rejects malformed, duplicate-broadening or unauthorized scopes. Tests are authored; execution is deferred.
4. **`ops-prod-secrets` — add repository/template secret-leak prevention** (Partial; Operations / Security). Root and generated-template CI now contain an immutable-digest-pinned Gitleaks scanner for source and reachable history, with no broad allowlists and suppressed/redacted scanner output. Synthetic-token unit regressions pass; two Docker-backed scanner fixtures are skipped and the actual local source/history scan is `NOT RUN` because Docker is unavailable. Secret-manager vendor, workload identity, production rotation, image/log scanning and least-privilege deployment remain owner decisions. Acceptance: a known secret fixture fails; safe placeholder/configuration passes; findings are redacted from logs and no real credentials are added.
5. **`msg-crash-window` — producer crash during broker outage** (Partial; Platform / Operations). A subagent is authoring the disposable RabbitMQ/EF/Wolverine crash-and-recovery drill described in task 16 below. It may close another technical fault window after exact CI evidence; business handler, consumer ACK, external effects and production partition/fencing approvals remain open.
6. **`msg-broker` — reject the built-in RabbitMQ guest principal in Production** (Partial; Platform). A subagent authored a fail-closed configuration fix: Production now rejects username `guest` case-insensitively even when its password is changed; regression cases and ADR-013 clarification are authored, not run. Approved topology/ACLs, principal membership, TLS trust and rotation, retry/DLQ policy, and Kafka schema/partition policy remain Operations-owned.
7. **`db-business-migrations` — enable owner-created migration tooling without business schemas** (Not implemented; Developer tools / actual context owner TBD). The `--with-db` context generator now authors a version-matched private EF Design dependency and provider-aware design-time factory, plus positive/negative tests and migration instructions. It deliberately generates no business model/migration; those still require a named context owner.

### P0 identity, contracts, and business ownership tasks

4. **`auth-membership` — define and enforce tenant membership** (Not implemented; Identity owner TBD). Existing JWT validation and one tenant claim establish token validity and requested scope; they do not prove that the subject belongs to that tenant. A generic placeholder port cannot close the acceptance gap. First appoint the Identity owner and choose the trusted IdP membership source, role/permission vocabulary, tenant-to-subject mapping, revocation behavior, and contract version. Then implement the membership resolver and API policy. Acceptance/tests: active member may access only its organization; absent member is denied; forged/stale membership or cross-organization privilege escalation is denied.
5. **`biz-contracts` — publish owner-approved V1 contracts** (Partial; business owners TBD). Choose the owning context, exact schemas, semantics, consumers, compatibility window, and evolution policy; publish versioned contracts and update ownership/context maps and catalog. Acceptance/tests: valid V1 producer/consumer exchange; malformed or incompatible shape rejected; private implementation references remain forbidden.
6. **`biz-contract-tests` — enforce consumer compatibility** (Not implemented; business owners TBD). Depends on task 5. The generic starter intentionally has no contract schema format or product V1/consumer catalog; adding a schema-neutral harness now would not prove compatibility. After owners choose a format and compatibility window, add provider/consumer fixtures and CI gate. Acceptance/tests: current V1 consumer passes; deliberately breaking V1 fails; equivalent V2 migration passes while V1 remains supported for its declared window.
7. **`biz-endpoints` — implement one approved product workflow** (Not implemented; actual context owner TBD). Requires written requirements naming the context, owned data, rules, routes, permissions, and lifecycle. Implement only that context's endpoints and persistence. Acceptance/tests: approved happy flow persists owner data; invalid/unauthorized request is rejected; owner invariants hold under duplicate and concurrent requests.
8. **`db-business-migrations` — add context-owned business schemas** (Not implemented; actual context owner TBD). A code-safe scaffolder increment is implemented and covered by passing generated-template tests: `new_context.py --with-db` creates provider-aware EF design-time tooling without creating business entities/migrations. Actual migrations depend on an approved domain model and task 7 scope. Add separate migrations for PostgreSQL and SQL Server without shared business tables or cross-context writes. Acceptance/tests: clean migration applies on each provider; invalid migration/rollback fails safely; schema ownership boundary remains intact.
9. **`db-concurrency` — define aggregate conflict behavior** (Not implemented; actual context owner TBD). Depends on task 7 and owner-selected conflict semantics. The empty context scaffold has no aggregate; choosing `rowversion`, PostgreSQL `xmin`, retry/merge behavior, or 409/412 mapping now would invent policy. Add concurrency tokens and map provider conflicts only after the owner defines the aggregate and caller response. Acceptance/tests: uncontended update succeeds; competing stale update gets the agreed conflict; first committed value remains and no last-writer-wins overwrite occurs silently.
10. **`msg-domain-consumers` — add owner-approved event consumers** (Not implemented; business owners TBD). Depends on tasks 5 and 7. Implement contract-backed inbox/handler behavior within each owning context and use explicit compensation/idempotency rules for external effects. Acceptance/tests: valid event changes only its owner's data; unauthorized cross-context write is impossible; duplicate/reordered event produces one business effect.

### P0 security, operations, and release tasks

11. **`auth-raw-sql` — close trusted tenant-session spoofing** (Partial; Platform + Identity/business owner + Security + DBA). Source review confirmed the RLS predicates read mutable PostgreSQL `set_config('aba.tenant_id', ...)` / SQL Server `SESSION_CONTEXT('aba_tenant_id')`, and the raw SQL callback currently runs under the same principal that can set those values. There is no safe regex/filter fix for arbitrary SQL. First define the trusted tenant-to-database authority and choose a least-privileged principal/session mechanism that the application role cannot spoof; then constrain raw/bulk SQL entry points and deploy owner-approved policies on both providers. Acceptance/tests: trusted tenant query succeeds; under the actual runtime role direct session spoof and cross-tenant bulk mutation fail; missing tenant cannot reach protected rows; pooled connections have no tenant residue and the app role cannot alter policies.
12. **`ops-prod-secrets` — select secret storage and rotation controls** (Not implemented; Operations / Security). Choose the supported secret manager, workload identity, rotation/overlap window, DB/broker least privilege, and audit requirements. Add provider adapter/configuration and a generated-project runbook. Acceptance/tests: secret is fetched through workload identity; revoked/expired secret fails closed; repository, image, and CI logs contain no secret values.
13. **`ops-audit` — establish independent audit custody and business event ownership** (Partial; Security / business owners). Existing code/tests cover HMAC append/verify, tamper detection, key rotation, and signed historical-prefix checks on both SQL engines. Choose immutable off-database checkpoint storage, offline signing key custody/rotation, database appender role, retention/legal rules, and each context's actor/action/outcome vocabulary; implement the sink/runbook only after those choices. Acceptance/tests: wrong key/signature, changed record, or deletion/truncation of an anchored prefix is rejected; later legitimate appends remain valid; business audit records contain actor/action/tenant/outcome without payload secrets. Document the limit: deleting records after the newest checkpoint cannot be detected until another anchor is issued.
14. **`dev-prod-deploy` — choose and rehearse the production target** (Not implemented; Operations / Security). Select cloud/hosting, DNS/TLS, identity and secret wiring, health/readiness routing, migration order, rollback and change-control. Add deployment pipeline and environment-specific smoke/rollback drill. Acceptance/tests: secure deployment becomes healthy; failed rollout returns to prior version; production secrets and tenant data stay isolated.
15. **`dev-release-sbom` — bind evidence to published artifacts** (Partial; Release Engineering). Root main CI already signs and verifies the exact tested NuGet template and scanned Docker-save archive; generated projects only build/scan and retain their own artifacts. Approve severity/unfixed-vulnerability policy, signing identity/trust, protected promotion, registry/package destinations, and deployment change-control. Then extend release workflow to verify/publish exact OCI registry and NuGet digests. Acceptance/tests: reviewed artifact publishes and verifies; altered or untrusted digest/policy violation blocks promotion; provenance subject equals the exact artifact being released.

### P0/P1 messaging, cache, and reliability tasks

16. **`msg-crash-window` — extend technical recovery to owned business effects** (Partial; Platform / Operations / context owners). A disposable PostgreSQL/RabbitMQ technical crash case is authored: commit a synthetic EF/Wolverine outbox command while the exact labeled RabbitMQ service is paused, kill only the child producer host, then recover with a fresh host and require one technical delivery/effect. The integration project builds locally with zero warnings; live execution is NOT RUN because Docker is unavailable. This still does not prove a real business handler/provider side effect, network partition/failover, or consumer ACK guarantee. Full closure needs a real owner handler, external side effect, approved broker partitions and multi-node fencing. Acceptance/tests: committed outbox eventually delivers after kill+broker recovery; kill at each approved boundary recovers; duplicate delivery cannot duplicate the business/external effect.
17. **`msg-broker` — approve and operate production broker topology** (Partial; Platform / Operations). A production RabbitMQ `guest`-username guard now passes local API unit tests; exact-head CI has verified duplicate effects on PostgreSQL and SQL Server for both RabbitMQ and Kafka, plus durable RabbitMQ outbox recovery. Select topology, ACL and tenant identity, TLS/secret rotation, retry/DLQ policy, schema/partition strategy, and capacity. Disposable-provider checks are technical evidence, not those approvals. Acceptance/tests: secure config connects; invalid/guest credentials/topology fail without secret leakage; outage recovers durable messages once under the approved retry policy.
18. **`cache-stampede` — set production coordination and loader constraints** (Partial; Platform / Operations / context owners). The technical code and exact CI coverage already include matching-settings cross-host miss collapse, shared concurrent/rate budgets, settings-drift rejection, coordinator outage fail-closed behavior, scope separation, and discarding a cancellation-blind loader's late result after lease loss. Remaining decisions are Redis failover, eviction, TTL, capacity/budgets, security/monitoring, representative deployed load, and which authorized queries may opt in. Require loaders to be read-only/idempotent and cancellation-aware because Platform cannot undo external side effects. Acceptance: matching replicas coalesce bounded misses; drift/outage never invokes the source; lease loss cannot return/cache a stale result or cross-scope data.
19. **`ops-load` — define measurable SLO and recovery targets** (Partial; Operations / Performance). Current CI measures only one generated API's `/health/live`: 40 requests, four workers, 3-second timeout, diagnostic p50/p95/RPS. Set owner-approved topology, realistic business workload, latency/throughput objectives, SLO, RTO/RPO boundaries, fault scenarios and safe capacity limits; then run a provider-backed two-replica workload/failover drill in that environment. Acceptance/tests: normal and peak load meet the target; overload and dependency failure trigger bounded behavior without corruption; replica/dependency recovery and data loss meet the agreed RTO/RPO.

### Execution order and test phase

- **Wave A (implemented and locally verified):** domain-neutral context scaffolding, one-use dead-letter inspection receipts, redacted dead-letter summaries, cache metrics, pinned secret scanning, Production RabbitMQ guest-username rejection, and a disposable broker crash/recovery drill are authored. The fresh template builds and its unit tests pass; the integration project builds. Live database, Redis, RabbitMQ, crash-drill, and Gitleaks container execution are `NOT RUN` locally because Docker is unavailable. Proceed with owner-gated task cards only after Identity, context, Security, DBA, Operations, and Release Engineering decisions are recorded.
- **Wave B (owner decisions):** tasks 4–10 and 11–15; publish the required Identity, context, Security, DBA, Operations, and Release Engineering decisions before business/production code.
- **Wave C (reliability):** tasks 16–19 after a real owning handler, broker/cache topology, workload, and SLO are selected.
- **Final verification:** completed local Python/governance checks, .NET 10 builds and unit tests, fresh generated starter HTTP smoke, NuGet package install/build/test, and release-attestation tests. Live-service checks remain `NOT RUN` locally and require the exact-head CI service matrix; update this queue and capability matrix from that evidence.

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
- **Status tracking:** the 55-capability JSON/README matrix and generated P0/P1/P2 backlog now pass six negative/positive checks plus a CI drift gate.
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
3. **EF Core production operations:** optional per-context empty DbContext scaffolding and a production-startup TLS verification guard now exist and passed direct + real Docker negative tests in [CI 43305238518](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43305238518). Provider-native disposable backup/restore drills are implemented; still needed: real context-owned models/migrations, optimistic concurrency, production point-in-time recovery, migration rollback, encrypted offsite copies, owner-approved RTO/RPO rehearsal and production certificate-chain validation.
4. **Redis resilience:** an opt-in authoritative-source fallback for noncritical reads and real-client network-failure test now exist; remaining: controlled live restart/reconnect, distributed invalidation, tenant authorization across services, key rotation, stampede protection and load testing.
5. **OpenTelemetry and operations:** select and secure a production collector destination, approve logs correlation and PII/sampling/retention policy, then complete health dependency readiness, performance baselines, load/failover tests, audit events and dashboards. The disposable CI Collector/export integration is already verified.
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

**Verification update:** [CI 43320400760](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43320400760)
passed 8/8 jobs, including 96 API xUnit cases, 24 actual SQL/Redis/Wolverine
integration cases (with HMAC audit and technical dead-letter replay on
both SQL engines), plus 7 native recovery tests. An owner-approved
operator IdP, immutable audit checkpoints, retention and business replay
policies remain open P0 gates.

## New P0 crash-fault and audit checkpoint hardening

Technical, opt-in SIGKILL drills have now been CI-verified against real
PostgreSQL and SQL Server: a flushed but uncommitted SQL receipt
must roll back when its child host dies, and a durably scheduled
Wolverine envelope must be processed after a new host starts.
The independent crash test category is explicitly CI-gated and
rejects non-disposable DB targets. CI run 43323597289 passed all
8 crash cases, including committed-effect-before-consumer-ACK redelivery. The remaining P0 crash acceptance gap
is kill in the same business Outbox commit/publish/ACK window,
duplicate delivery and consumer/external side-effect idempotency.

The audit HMAC chain now has a separately signable checkpoint
candidate and a historical-prefix verifier. Security/Operations
must still deploy an actual immutable offsite checkpoint sink,
isolate and rotate the offline signing key, set retention and
operator permissions and audit actual context-owned decisions.

**ACK-loss progress (verified on both SQL engines):** a third
SIGKILL case now commits an idempotent technical receipt, kills
the worker before handler ACK, and requires restored Wolverine
to redeliver without duplicating the receipt. This still needs
validation against the owning business domain's external effects,
publisher Outbox and broker transports.

**Full fault-injection evidence:** [run 43323597289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43323597289) passed 8/8 jobs, with 99 default API xUnit tests, 26 live SQL/Redis/Wolverine integration tests, 7 recovery drills and 8 actual process-kill cases. Real business Outbox publication, externally compensated consumers and immutable checkpoint custody remain unfinished.

## New cache/OCI technical hardening (P1/P0)

- Redis: host-wide scope/key coalescing, correct value-type/null miss
  differentiation, cancellation/fault lease cleanup and a separate
  actual redis:7-alpine stop/start CI drill are implemented. Multi-node
  miss prevention, distributed invalidations and strict source-load
  budgets while Redis is down remain open.
- OCI: reviewed .NET 10 Noble SDK/runtime manifest digest pins,
  standalone fail-closed Dockerfile/lock validation and negative
  tests, and upstream signed archive of the scanned Docker image
  are implemented. The signature binds a Docker-save artifact,
  **not a deployed OCI registry digest**. HIGH/unfixed advisories,
  protected production publishing and release approvals remain P0.
- Follow current CI status in docs/implementation-status.json and the
  latest root Actions run. Do not infer all new jobs passed from an
  earlier partial pipeline.
