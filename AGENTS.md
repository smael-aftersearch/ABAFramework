# ABA .NET 10: repository instructions for coding agents

**Primary delivery goal:** Create reusable, generic .NET 10 APIs. A new product must be started with `templates/aba-api` (`dotnet new aba-api`) and must NOT inherit Academy's example business domains. The Academy module exists only as an optional isolated reference. Validate template changes with `python3 scripts/test_starter_template.py`, `bash scripts/verify-starter.sh`, `bash scripts/verify-container-starter.sh`, and `bash scripts/pack-template.sh`. Keep source, tests, AI rules and docs synchronized.

**Purpose:** Build and evolve an Adaptive Business Architecture (ABA) modular-monolith framework and its Academy example without increasing future cost of change.

**New generic API agent workflow:** Each project created with `dotnet new aba-api` carries `tools/aba_task.py`, a local `AGENTS.md` and `.agents/skills/aba-engineering/SKILL.md`. Use `python3 tools/aba_task.py plan --task "<request>" --file <path>`, `verify --level static/build` and `--level full` for real infrastructure, plus the project's generated CI. The script never assigns unproven business ownership and redacts known connection strings from evidence.

**Capability/status truth:** Before claiming an implementation is complete, read `docs/implementation-status.json` and `docs/IMPLEMENTATION-STATUS.md`. After any material feature change, update the JSON, then run `python3 scripts/status_matrix.py --write`, `python3 scripts/test_status_matrix.py` and `python3 scripts/status_matrix.py --check`. The generic table and optional Academy Phase-1 matrix are different products and must never be merged.

**Start here:** `docs/ai/INDEX.md` (knowledge map). The reusable workflow is `.agents/skills/aba-engineering/SKILL.md`. For task-oriented reading suggestions run `python3 scripts/ai_check.py brief --task "<request>"`. Read only the context and references relevant to the task; do not read all 78 PDF pages on each small change.

## Non-negotiable architectural rules

1. **Find the owning business context before code.** Data, rules, decisions, and public contracts each have exactly one owner. Context metadata lives under `samples/Academy/<Context>/_Docs/`.
2. **Contracts are the public boundary.** Do not reference another business context's Domain/Application/Infrastructure or write its storage. Use `Academy.<Owner>.Contracts` with explicit `V1`/`V2` versioning. `Academy.Host` composes modules but does not decide business policy.
3. **Keep Domain pure; keep Framework technical.** Domain must not depend on EF Core, HTTP, Redis, Wolverine, provider SDKs or other context implementations. Framework must not own enrollment/payment/accounting policy.
4. **Synchronize docs with code.** Follow `docs/ai/DOC-UPDATE-RULES.md`: update ownership/context-map/contract catalog, OpenAPI, relevant ADRs and evolution notes when they actually change. Never edit metadata to conceal a forbidden dependency.
5. **Preserve critical invariants.** Payments and message consumers are idempotent; posted accounting journals cannot be edited; Enrollment owns capacity and activation decisions; Reporting is a projection, not a source of truth.
6. **State implementation honestly.** The *optional Academy sample* still uses local file messaging and memory cache. The **generic API template** separately includes real EF Core 10 adapters for PostgreSQL **and SQL Server**, Redis and Wolverine 6 with successful DB/Redis/message probes in CI. OpenTelemetry and optional JWT tenant-claim guard are included. Additional CI now verifies technical Outbox commit/rollback, durable clean restart, Keycloak signed JWT/OIDC 204/401/403 and EF tenant read/write guards on both SQL engines. The generic starter now also has **opt-in technical** PostgreSQL/SQL Server RLS policy builders, trusted tenant transaction scopes and real direct-SQL/EF bulk negative tests. These are **not** owner-approved production RLS migrations, trusted IdP membership, business Outbox commit/publish/ACK crash-window proof or Production readiness. Generic CI now also verifies the opt-in audited Wolverine dead-letter operator and HMAC audit chains, with independent signed checkpoint support. Actual SIGKILL fault-injection is a separately opted-in disposable CI-only gate; do not claim it covers live customer handlers or broker ACK windows.
7. **Do not regenerate metadata blindly.** `scripts/generate_metadata.py` is a bootstrap-only seed writer, not a live source-of-truth sync tool; it refuses overwriting without `--force`. Review ownership changes explicitly.
8. **No unsafe side effects without approval.** Never commit secrets, expose the unauthenticated development server publicly, use real payment providers, alter production data or deploy without explicit authorization.
9. **Treat testing as part of every material task.** Read `docs/testing/TEST-STRATEGY.md` and `tests/README.md`. Before implementation state happy-path, negative and invariant acceptance cases; create/update the tests that exercise changed behavior, and do not remove failing assertions to hide defects. Always distinguish code-authored tests from tests actually executed.

## Executable task and test protocol

For each material request, run `python3 scripts/task_protocol.py plan --task "<request>" --file "<changed/path>"` to suggest business-context candidates, technical component owners, reading sources and success/negative/invariant test cases. These are separate candidate lists: Platform/Host ownership does not imply business ownership. Confirm every candidate from metadata and source; a suggestion is not permission. Implement source + focused tests + relevant docs together. Run `python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json` and where .NET 10 exists use `--profile all --output artifacts/verification/full.json`; the runner records PASS, FAIL and NOT_RUN and fails closed on missing runtime. Read `.agents/skills/aba-engineering/references/testing-and-evidence.md` for complete guidance.

## Before finishing a material task

Run `python3 scripts/test_task_protocol.py`, `python3 scripts/task_protocol.py verify --profile static` and relevant happy-path/negative tests. The verifier executes AI readiness, architecture governance, their negative tests and the C# structural scan; use `--profile all` for .NET restore/build/smoke. Use `bash scripts/verify-all.sh` for the complete stack when .NET 10 is available. For HTTP/API/host changes, build Release and run `bash scripts/verify-http.sh` to prove the loopback server starts and the API demo completes; never treat that local test as approval for public deployment. Report command results precisely; missing SDK means .NET build/runtime remains unverified.

Finish with an **Architecture impact + Test evidence** report: owner, contracts, dependencies, updated docs, acceptance cases, test paths, exact commands with PASS/FAIL/NOT RUN, and remaining risks. Use `.github/pull_request_template.md`.

## Opt-in technical audit and dead-letter boundary

The Platform supplies an HMAC-chain audit ledger and a gated service-only
Wolverine dead-letter inspector/replay API. Business contexts must
approve audit actions, trusted actor/tenant membership, external
checkpoints, key retention and idempotent consumer compensation. Never
expose Wolverine's native dead-letter HTTP endpoints or auto-grant
operator claims. Read template ADR-006/ADR-007 and operations runbook
before changes; verify each provider with real SQL and a synthetic
poison envelope before claiming recovery coverage.

## Cache and OCI source-of-truth boundaries

The generic read-through cache defaults to a host-wide per-scoped-key
load gate. Its opt-in Redis coordinator adds best-effort distributed
miss leases and shared source-load budgets; neither grants tenant
permission nor enforces business correctness. Distributed coordination
fails closed without a live permit and requires identical settings for
every replica sharing its namespace. The isolated
RedisRestartDrill must only stop its exact CI-provisioned redis:7-alpine
container and is excluded from standard tests. For release changes,
the Dockerfile and oci-base-lock.json must agree exactly; both
upstream and generated CI run fail-closed OCI base-lock tests.
The root-signed Docker-save archive is not an OCI registry release.
Read template ADR-010/ADR-011 before altering these assumptions.
