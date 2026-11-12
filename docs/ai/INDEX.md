> **Updated evidence (2026-11-12):** [five-job successful run](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43301970172) verifies 60 default API xUnit tests, 16 real SQL/Redis/Wolverine integration tests, Keycloak, Docker, template packaging and the portable ABA task runner. A later template update bundles CI workflows into generated projects; check its exact run for final verification.
# AI knowledge entrypoint — ABA .NET 10

This is the **navigation map** for humans and coding agents. **For any new customer/product API, prefer the domain-neutral `templates/aba-api` starter, not the optional Academy example.** Its generated AGENTS.md and docs are scoped to the new repository. The repository provides a portable engineering skill at `.agents/skills/aba-engineering/SKILL.md` and permanent rules at `/AGENTS.md`. Read the relevant documentation for the request; do not assume an AI automatically reads every file.

## Sources of truth (use at different levels)

| Source | Purpose | When to read |
| --- | --- | --- |
| `AGENTS.md` | Mandatory repository-level do/don't and verification | Every coding session in a supported agent |
| `docs/implementation-status.json` | Machine-readable generic-template capabilities, current state, owner, evidence and remaining tasks | Every release/capability/roadmap change |
| `docs/IMPLEMENTATION-STATUS.md` | Generated readable implementation matrix + prioritized P0/P1/P2 backlog | Roadmap, planning and status requests |
| `scripts/status_matrix.py` and `scripts/test_status_matrix.py` | Fail CI on stale tables, fake missing source/tests and invalid status | Every feature status change |
| `.agents/skills/aba-engineering/SKILL.md` | Task execution procedure | Code, architecture, API, infrastructure and docs changes |
| `docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf` | Authoritative **ABA principles** (Ch. 6-9, 16-24, 38-42, 53-55) | New architectural decisions or rule conflicts; selected chapters |
| `docs/architecture.md` | Current Phase-1 implementation and constraints | Context, cross-context flow, persistence/messaging changes |
| `*/_Docs/ownership.yaml` | Owner of data, rules, decisions and public contracts | Before editing a business capability |
| `*/_Docs/context-map.yaml` | What is public, consumed, allowed and forbidden | Before a context integration or dependency change |
| `*/_Docs/ai-rules.yaml` | Context-specific agent constraints | Before editing a context |
| `*/_Docs/{decisions,evolution}.md` | Per-context rationale and evolution | Behavioral changes and migrations |
| `contract-catalog.yaml` | Public contract versions, owners, consumers | Changing integration events and APIs |
| `docs/decisions/ADR-*.md` | Approved tradeoffs and historical reasons | New technical/ownership design |
| `docs/ai/DOC-UPDATE-RULES.md` | What documentation must stay synchronized | Before completing a material task |
| `docs/testing/TEST-STRATEGY.md` and `tests/README.md` | Mandatory test matrix, existing versus planned suites, actual commands and evidence | **Before every material behavior change or bug fix** |
| `scripts/task_protocol.py` and `scripts/test_task_protocol.py` | Task-aware QA planning, evidence, regression tests | Before coding and completing material tasks |
| `.agents/skills/aba-engineering/references/testing-and-evidence.md` | Scope, evidence limits and refinement workflow | When triaging tests, CI and agent behavior |
| `docs/NEXT-MILESTONES.md`, `docs/KNOWN-LIMITATIONS.md` | Unimplemented capabilities and risks | When claiming features or planning changes |
| `scripts/governance.py` | Executable ownership/contract checks | Every material code change |

The book's example domains are illustrations, not mandatory universal contexts. In this sample, Identity, Enrollment, Payments, Accounting, and Reporting are actual owners. Framework libraries are technical capabilities, never business owners.

## Quick start for an agent

```bash
python3 scripts/ai_check.py brief --task "add a payment refund event"
python3 scripts/task_protocol.py plan --task "add a payment refund event"
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json
# Requires a working .NET 10 SDK:
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json
```

**Every material change must start with a test plan and finish with test evidence** as defined in `docs/testing/TEST-STRATEGY.md`. The `brief` command **suggests** relevant files using task keywords and target paths. Its guesses are not authoritative; inspect the actual ownership metadata. If owner cannot be determined, do not invent one.

For a short Persian operator guide, see `docs/ai/USAGE-FA.md`. Read `docs/ai/CHANGE-PROTOCOL.md` for the end-to-end task loop and `docs/ai/DOC-UPDATE-RULES.md` for documentation maintenance. The machine-readable `docs/ai/knowledge-map.json` powers document discovery; update it when adding or moving a source that agents should discover.

## Implementation truth and confidence

The **optional Academy Phase-1 sample** has local file messaging and memory cache. The **generic template** has real optional EF Core 10 PostgreSQL/SQL Server, Redis caching, Wolverine relational durable messaging and OTel wiring; GitHub Actions verified both SQL engines, Redis and a Wolverine technical message handler. Ephemeral real Keycloak/JWKS 204/401/403, PostgreSQL/SQL Server EF tenant query/write guards, technical Outbox pre-commit rollback and clean restart recovery are now verified in CI. Production membership authorization, SQL-level RLS, poison/dead-letter replay, arbitrary process crashes and real business context Outbox remain incomplete. The original authoring environment lacked .NET, but GitHub Actions now runs .NET build, xUnit and real service integration tests. **Do not turn planned architecture into a statement of delivered behavior**. See `VERIFICATION-REPORT.md`.

## Example cross-context rule

`PaymentCapturedV1` is evidence published by Payments. Enrollment decides whether to activate, Accounting decides how to post a journal, and Reporting updates its read-side projection. Payments must not write their tables or call their domain implementations.
