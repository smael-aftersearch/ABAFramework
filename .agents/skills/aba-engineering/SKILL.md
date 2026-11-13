---
name: aba-engineering
description: Apply Adaptive Business Architecture (ABA) while implementing, reviewing, refactoring, testing, or documenting C#/.NET 10 enterprise systems, particularly this ABA modular-monolith framework and Academy reference app. Trigger on requests to change bounded contexts, business rules, public Contracts, messaging/outbox, persistence, caching, security, APIs, CI, tests, documentation, or agent instructions. Determine owning context; read task-relevant repository metadata and ADRs; update code and documentation together; run executable governance. Do not apply to unrelated repositories.
---

# ABA Engineering: document-driven coding protocol

Use this skill as the procedural layer for the ABA repository. It does not replace the repository's own source of truth. Keep prompts small; load documentation progressively.

## 0. Find the repository and establish facts

1. Locate the repository root containing `AGENTS.md`, `docs/ai/INDEX.md`, and `contract-catalog.yaml`.
2. Read `AGENTS.md` and `docs/ai/INDEX.md`. If they are absent, say so; never assume that this skill's examples describe an unknown repository.
3. Run `python3 scripts/ai_check.py brief --task "<request>"` and `python3 scripts/task_protocol.py plan --task "<request>"` from that root when available. Pass `--file <path>` for likely edits. These are **reading/test suggestions, not ownership authority**; confirm against metadata, ADRs and code.
   The task plan inventories xUnit projects under `tests/` and `templates/*/tests/`; listed projects exist in source but are not evidence that tests ran.
4. Check the actual code and configuration for implemented behavior. The reference PDF defines ABA principles; the accepted ADRs and current metadata describe explicit project decisions. A README or aspirational specification does not prove a feature exists.

## 1. Make a minimal ownership-aware plan

For a material change, write a short internal or user-visible plan with:
- request and acceptance criteria;
- the **single owning context** of each affected business decision, data item, rule, and contract;
- affected internal modules, published contracts, and consumers;
- security, rollback, migration, and documentation impacts;
- chosen happy-path, negative and invariant tests, existing versus planned suites, and commands to verify the change.

If ownership or requirements remain ambiguous, investigate code, metadata, ADRs, and the book. For destructive, financially sensitive, or boundary-breaking decisions, request explicit approval rather than inventing ownership. Prefer a safe, scoped change to speculative architecture.

## Generated-project agent protocol (new)

- The **generic generated API** now contains `tools/aba_task.py` and `docs/ai/TASK-PROTOCOL.md`: run `python3 tools/aba_task.py plan --task "<request>" --file <affected/path>` before edits and `python3 tools/aba_task.py verify --level static` and `--level build` afterward. For SQL, Wolverine and Redis changes, use `--level full` against live PostgreSQL/SQL Server/Redis; missing connections must return NOT_RUN/nonzero.
- Generated `.github/workflows/ci.yml` runs unit and architecture checks; `integration.yml` is manually dispatched for real services. CI does not imply a deployed production system or authorization to publish.
- Redis outage bypass is opt-in for non-authoritative read-through queries only. Never use it for financial rules, authentication, tenant authorization or idempotency. Rate limits partition authenticated JWT users, not spoofed tenant/proxy headers. Keep Health unaffected by quotas.

## Generic API quick-start first

- For a request to create a **new project or blank API**, use `templates/aba-api`, not the Academy business sample.
- Generate using `dotnet new install ./templates/aba-api` and `dotnet new aba-api -n <Actual.Product.Name> -o <target-folder>`; do not invent sample business domains.
- A template change must update generated-project docs and relevant xUnit tests. Verify with `python3 scripts/test_starter_template.py` and `bash scripts/verify-starter.sh` in an SDK-capable environment. The latter must generate, build, test and launch a fresh project.
- Academy-specific metadata checks remain relevant only when changing `samples/Academy`.
- Confirm generated-project CI before claiming capabilities. EF Core 10 dual-provider, Redis and Wolverine adapters now exist and have real service baseline tests; do NOT promote those probes to claims of crash-safe business transactions, complete tenant isolation or production readiness.

## 2. Read only relevant documents

- All implementation tasks: `AGENTS.md`, `docs/ai/INDEX.md`, **`docs/testing/TEST-STRATEGY.md`**, `tests/README.md`, and affected source files and tests.
- Business context work: that context's `_Docs/ownership.yaml`, `context-map.yaml`, `ai-rules.yaml`; read `decisions.md` and `evolution.md` for design/migration changes.
- Events/contracts: `contract-catalog.yaml`, publisher + consumer context maps, public Contracts projects; consult book Ch. 8, 17, 38.
- Security: `docs/security.md`, Identity ownership, Platform.Security design; consult book Ch. 18.
- Persistence/caching/messaging: `docs/architecture.md`, relevant Platform adapters and ADRs; consult book Ch. 16-17 and migration plans.
- Cross-context restructuring or new principles: `docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf` (targeted chapter) and `docs/decisions/`.
- Documentation maintenance: `docs/ai/DOC-UPDATE-RULES.md`.

For the decision logic behind these requirements, read [ABA authority rules](references/aba-rules.md). For common task-specific reading and updating, read [task playbooks](references/task-playbooks.md). For evidence gates and skill improvement, read [testing and evidence](references/testing-and-evidence.md).

## 3. Mandatory design constraints

- **One owner** for each business datum, business invariant, decision, and published contract.
- Other contexts may use the owner's versioned **Contracts**, not the owner's Domain, Application, Infrastructure, data tables, or EF DbContext.
- Domain models must not import EF Core, ASP.NET, Redis, Wolverine, or external provider SDKs.
- Framework/Platform provides technical capabilities; it does not own business policy.
- Use a versioned new public contract for incompatible changes; do not silently mutate `V1`.
- Do not share business entities or implement cross-context data writes as a shortcut.
- Keep accounting posted journals immutable; use compensating records for corrections.
- Keep payment callbacks and message redelivery idempotent; use transactional Outbox/Inbox for real durable distributed implementations.
- Distinguish the **Academy legacy sample** (local substitutes) from the **generic template** (real, opt-in PostgreSQL/SQL Server, Redis, Wolverine, OpenTelemetry and JWT tenant-claim components). Report exact CI evidence and remaining production gaps; real disposable Keycloak OIDC and EF-level tenant guards are verified, but do not invent production membership, SQL RLS, ungraceful crash recovery or full business Outbox reliability.
- Reject secret leakage, unsafe auth bypasses, real payment execution without approval, and unverifiable security claims.

## 4. Implement code, tests, and docs in the same change

1. Modify the owning context and its internal layers; integrate cross-context behavior only through public Contracts or explicit application APIs.
2. **Define a test plan before code**: a success case, relevant failure/negative cases and impacted invariants. Use task_protocol.py plan as a checklist, not as proof of coverage. For a bug, create a reproduction test first when feasible. Add/update owner-context unit tests, event/consumer/integration tests or architecture negative tests as required by `docs/testing/TEST-STRATEGY.md`. Do not bypass contracts or delete failing assertions merely to pass CI. Phase 1 has a console smoke runner, not xUnit; future xUnit projects are planned, not present.
3. Update docs **when the corresponding reality changes**. Use the matrix in `docs/ai/DOC-UPDATE-RULES.md`; update context metadata and catalog when ownership/contract/dependency changes, ADRs for consequential design choices, README/OpenAPI for public behavior, and this skill only when the agent workflow itself changes.
4. Never rewrite documentation merely to conceal an architecture violation. Change the code or record the unresolved conflict.
5. Avoid unrelated refactors and needless framework abstractions; show how complexity reduces future change cost.

## 5. Execute bounded checks and preserve evidence

From the repo root, run narrow affected tests first, then:

```bash
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json --markdown artifacts/verification/static.md
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json --markdown artifacts/verification/full.md
```

`static` executes AI checks, negative governance, tool regression tests and a structural scan. `all` additionally requires a working .NET 10 SDK and executes restore/build/console smoke. If a gate cannot run, record **NOT_RUN**, not PASS. Nonzero verifier exit codes mean a failed or incomplete profile. Use `bash scripts/verify-all.sh` for the same full CI-oriented gate. The original Academy evidence runner only checks its own Phase-1 scope. The separate generic-template CI now tests xUnit, real database providers, Redis and a Wolverine technical message; it now verifies Keycloak OIDC, EF-level multi-tenant query/SaveChanges guards, and injected pre-commit transaction rollback on both SQL providers; it still does **not** certify SQL RLS, IdP membership, arbitrary kill/restart crash windows or full business Outbox reliability. Never invent coverage or claim production readiness.

Complete every material task using this compact report; see [evidence and drift protocol](references/evidence-and-drift.md):

```text
ABA task report:
- Confirmed owner and decision rationale:
- Changed code, tests and docs:
- Contracts, dependencies, security and migration impact:
- Acceptance cases (happy / negative / invariant), with test paths:
- Actual verification evidence path and PASS/FAIL/NOT_RUN per gate:
- Unverified risks and smallest next action:
```

## 6. Self-maintaining documentation, with boundaries

- Discover stale/missing instructions while working; correct them alongside the affected change when verified.
- Prefer short, indexed, task-specific documents over one huge prompt. Do **not** read or rewrite the whole 78-page PDF for every trivial task.
- When sources disagree, identify the exact discrepancy and impact. Preserve original source evidence and propose a documented resolution; never silently overwrite the book, falsify status, or rename ownership to make validation pass.
- Machine-readable context metadata must remain valid JSON within YAML 1.2 in this starter. Keep documentation synchronized with actual code, not aspirational state.
- Source changes cannot be fully enforced by instructions alone; use CI and governance as the independent backstop.


## 7. Improve the Skill only with reproducible evidence

When a task reveals a recurring documentation gap, invalid heuristic or missed rule, add a failing regression fixture first, update the smallest related script/map/playbook, and re-run validators. Never make a test pass by weakening an invariant. Updating a repository copy or producing `skill.zip` does not itself replace an already installed Skill in other agents.
