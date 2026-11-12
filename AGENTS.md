# ABA .NET 10: repository instructions for coding agents

**Purpose:** Build and evolve an Adaptive Business Architecture (ABA) modular-monolith framework and its Academy example without increasing future cost of change.

**Start here:** `docs/ai/INDEX.md` (knowledge map). The reusable workflow is `.agents/skills/aba-engineering/SKILL.md`. For task-oriented reading suggestions run `python3 scripts/ai_check.py brief --task "<request>"`. Read only the context and references relevant to the task; do not read all 78 PDF pages on each small change.

## Non-negotiable architectural rules

1. **Find the owning business context before code.** Data, rules, decisions, and public contracts each have exactly one owner. Context metadata lives under `samples/Academy/<Context>/_Docs/`.
2. **Contracts are the public boundary.** Do not reference another business context's Domain/Application/Infrastructure or write its storage. Use `Academy.<Owner>.Contracts` with explicit `V1`/`V2` versioning. `Academy.Host` composes modules but does not decide business policy.
3. **Keep Domain pure; keep Framework technical.** Domain must not depend on EF Core, HTTP, Redis, Wolverine, provider SDKs or other context implementations. Framework must not own enrollment/payment/accounting policy.
4. **Synchronize docs with code.** Follow `docs/ai/DOC-UPDATE-RULES.md`: update ownership/context-map/contract catalog, OpenAPI, relevant ADRs and evolution notes when they actually change. Never edit metadata to conceal a forbidden dependency.
5. **Preserve critical invariants.** Payments and message consumers are idempotent; posted accounting journals cannot be edited; Enrollment owns capacity and activation decisions; Reporting is a projection, not a source of truth.
6. **State implementation honestly.** Phase 1 currently uses local file messaging and memory cache. PostgreSQL, Wolverine, Redis and OAuth/OIDC are future milestones, not implemented guarantees. It is not production-ready.
7. **Do not regenerate metadata blindly.** `scripts/generate_metadata.py` is a bootstrap-only seed writer, not a live source-of-truth sync tool; it refuses overwriting without `--force`. Review ownership changes explicitly.
8. **No unsafe side effects without approval.** Never commit secrets, expose the unauthenticated development server publicly, use real payment providers, alter production data or deploy without explicit authorization.
9. **Treat testing as part of every material task.** Read `docs/testing/TEST-STRATEGY.md` and `tests/README.md`. Before implementation state happy-path, negative and invariant acceptance cases; create/update the tests that exercise changed behavior, and do not remove failing assertions to hide defects. Always distinguish code-authored tests from tests actually executed.

## Executable task and test protocol

For each material request, run `python3 scripts/task_protocol.py plan --task "<request>" --file "<changed/path>"` to suggest owner(s), reading sources and success/negative/invariant test cases. Confirm every proposed owner from metadata and source; a suggestion is not permission. Implement source + focused tests + relevant docs together. Run `python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json` and where .NET 10 exists use `--profile all --output artifacts/verification/full.json`; the runner records PASS, FAIL and NOT_RUN and fails closed on missing runtime. Read `.agents/skills/aba-engineering/references/testing-and-evidence.md` for complete guidance.

## Before finishing a material task

Run `python3 scripts/test_task_protocol.py`, `python3 scripts/task_protocol.py verify --profile static` and relevant happy-path/negative tests. The verifier executes AI readiness, architecture governance, their negative tests and the C# structural scan; use `--profile all` for .NET restore/build/smoke. Use `bash scripts/verify-all.sh` for the complete stack when .NET 10 is available. Report command results precisely; missing SDK means .NET build/runtime remains unverified.

Finish with an **Architecture impact + Test evidence** report: owner, contracts, dependencies, updated docs, acceptance cases, test paths, exact commands with PASS/FAIL/NOT RUN, and remaining risks. Use `.github/pull_request_template.md`.
