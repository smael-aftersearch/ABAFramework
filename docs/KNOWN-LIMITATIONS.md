> **Generic API current evidence (2026-11-13):** [successful .NET 10 full CI](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) verifies generated API tests, both PostgreSQL and SQL Server, Redis, Wolverine technical Outbox tests, real Keycloak tokens, non-root Docker and NuGet install. The exact implemented/partial/not-yet-implemented matrix and acceptance checklist are [here](IMPLEMENTATION-STATUS.md). Production remains gated by real business authorization/RLS, crash/dead-letter recovery, backup/restore, collector/export validation, security review, deployment and release controls. The numbered Phase-1 notes below apply **only to the optional Academy example**, not the generic template.
# Known limitations and security warnings

## Optional Academy Phase 1 — historical example constraints

These historical warnings describe the Academy demo (file Outbox and in-memory Cache), **not** the technical generic API starter. See the current [generic framework status catalog](IMPLEMENTATION-STATUS.md) for current provider and security implementation.

1. **C# not compiled in authoring environment:** .NET 10 SDK is absent, network DNS unavailable. Source has been authored and checked by Python structural validation only. Do not interpret the ZIP as a verified build.
2. **No production authentication or authorization:** this is a DEVELOPMENT-ONLY demo. The host aborts on non-development environments. Never bind it to a public IP, put behind a public reverse proxy, or connect real user data.
3. **Single-process local persistence:** atomic JSON-file replacement is an educational approximation of a local transaction. Two processes sharing a data directory risk lost updates. There are no database transactions, isolation, pooling, migrations, or failover.
4. **Local event delivery only:** the dispatcher is in-process, not Wolverine or RabbitMQ. Dead letters are stored but there is no admin API/CLI for replay. Delivery has no external ordering or message schema registry.
5. **Fake payment gateway:** there is no real charge, settlement, refund, callback signature verification or provider-side idempotency/reconciliation. Never connect a real payment processor to the current implementation.
6. **Simplified Accounting:** illustrative balanced append-only postings only, no chart-of-accounts governance, taxes, fiscal period lifecycle, reversal commands, currency conversions or compliance guarantees.
7. **In-memory caching:** not Redis and does not share invalidation across hosts; no tenant isolation (single demo tenant only). Avoid production use.
8. **Static OpenAPI:** manually maintained, not generated from endpoints; drift is not yet automatically checked.
9. **Minimal tests:** dependency-free console smoke checks are authored but not run; production integration, security, concurrency, contract-compatibility and stress tests remain outstanding.
10. **Governance coverage:** Python checks metadata, project references and simple source patterns. It is not yet an IL analyzer and cannot prove the absence of all dynamic cross-context access or security vulnerabilities.
11. **No distributed transactional consistency:** handlers may partially succeed; replay suppression uses inbox IDs and domain guards. The demo is not a financial-grade distributed system.
12. **Potential performance constraints:** full-file read/serialize/write per operation, in-memory event polling and non-evicting expired cache entries until read/removal; intentionally simple for an initial slice.

13. **Waitlisted payments:** the illustrative flow captures before making the final capacity decision; a payment may be recorded for a waitlisted participant. A production system must reserve seats before capture or define safe void/refund/compensation rules.

## Agent Skills / documentation limitation (2026-11-12)

The repo includes agent navigation, a portable Skill and static policy tests. These do not guarantee every AI platform discovers or follows those instructions, and cannot infer all semantic documentation drift from source code. Skills only execute when invoked by a supported agent that has access to the repository. The doc index and metadata are maintained when making tasks; CI checks the structure but does not replace human review of business meaning.

The `scripts/generate_metadata.py` program is a **bootstrap seed writer** and must not be used as a live synchronizer: by default it refuses to overwrite existing metadata, and `--force` requires deliberate review.

## AI test planning / evidence runner limitations (2026-11-12)

The Skill's task planner uses keywords and changed-path hints. It **does not**
assign authoritative ownership, prove which tests cover a change, or create
missing xUnit/integration tests. The verification runner executes fixed local
checks and writes evidence, but it **does not** run in the background or
certify production readiness. In this environment six Python gates passed and
three .NET steps were NOT_RUN due to absent SDK. Check `VERIFICATION-REPORT.md`.

## Supply-chain and signed release limits

The generic API starter now includes authored NuGet vulnerability checks
and an SPDX 2.3 resolved-package SBOM in CI for the default solution.
The default solution gate passed 13 Python regressions and produced a 161-package SPDX inventory in CI run 43317295032; it is executable evidence, not production certification.
The external integration-only test project is not in that solution;
pinning/scanning OCI digests and signing/verifying release attestations
have not been completed. Existing Production gates still apply.

## 2026-11-13 — CI recovery and expanded software supply-chain scope

Isolated native PostgreSQL and SQL Server backup/restore drills are now authored
and wired to live disposable infrastructure in CI. Running them **never
means production backups are configured**: real per-context business schema
restores, point-in-time recovery, encrypted offsite retention, migrations
rollback, failover and RTO/RPO drills still need an owner-approved environment.
The fixtures explicitly reject non-loopback/foreign databases and require
both CI and a deliberate opt-in flag. A PostgreSQL restore-client version mismatch
was discovered during the first trial; the drill now uses matching PostgreSQL
16 tools in Docker.

The separate integration-test project's restored NuGet graph now receives
its own vulnerability audit and SPDX artifact, alongside the default solution.
Container scanning/digest pinning and signing of release artifacts remain open.
Inspect the newest run for executable evidence; no generic technical probe
certifies a production release.

**Latest recovery evidence:** [CI run 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) passed 7/7 isolated restore-and-guard tests, 16/16 existing service tests, and two independent NuGet audits. Production restore points, encrypted retention, schema migration rollback, RPO/RTO, container image scans and artifact attestations remain release gates.

## Additional RLS and signed release limitations

The generic template now includes provider RLS builders and a parameterized
tenant transaction scope, but **does not** install any customer's table
policy, authorize user membership, eliminate SQL-injection-based tenant
session spoofing or provision least-privileged production database roles.
The PostgreSQL real test runs as a non-superuser, while the disposable SQL
Server fixture uses an administrator; DBA bypass and production grants
need separate testing.

A commit-pinned Trivy final-runtime image scan blocks fixable CRITICAL
vulnerabilities in CI, and an upstream main-branch job is configured to
attest/verify the tested NuGet template with GitHub/Sigstore identity.
Neither is proof of public OCI signature, immutable base-image digest
pinning, production secrets and TLS, or policy approval for unpatched/
HIGH vulnerabilities. Always inspect the exact CI run.

**Verified technical RLS and release-gate evidence:** [CI 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032) passed 8/8 jobs, including real database row-filter and EF bulk denial tests, a runtime container vulnerability scan, and signed Sigstore/SLSA provenance for the template package with a corrupted-artifact negative case. Unrestricted SQL capable of changing tenant session context, privileged database roles, actual tenant memberships, published OCI image provenance and base digest pinning remain unresolved.

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
