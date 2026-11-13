# Document-driven ABA change protocol

A coding agent uses this loop for every material request. This is a workflow contract; the repo's machine-readable metadata and tests protect the architecture independently of whether the agent follows prose instructions.

## Discover -> Decide -> Implement -> Synchronize -> Verify -> Report

**Discover:** Read root `AGENTS.md`, this index and task-relevant paths from `python3 scripts/ai_check.py brief --task "..."`. Generate a proposed test matrix with `python3 scripts/task_protocol.py plan --task "..."`. The plan's test inventory includes checked-in xUnit projects under `tests/` and `templates/*/tests/`; inventory means source exists, not that tests ran. Neither command grants architectural authority; confirm in metadata, ADRs and code.

Plans keep business-context candidates separate from technical component owners such as Host and Platform. A technical component can own its adapter or composition code without owning a business rule, data item or contract. Path-based candidates remain suggestions and must be checked against the source and relevant ADRs.

**Decide:** Find the single authority for every business datum, rule, decision and public contract. Read `docs/testing/TEST-STRATEGY.md`, then define a test plan (success, failure/negative and invariant cases) before coding. Establish expected behavior, API/error cases, failure semantics, migrations, security, compatibility and tests. If ownership is ambiguous, inspect the map/catalog and prior ADRs. Request an explicit decision before a material ownership change instead of creating a shared model.

**Implement:** Add/modify tests alongside code, with regression and failure coverage. Keep Domain independent, Application orchestration local, Infra adapters behind interfaces, and public Contracts versioned. Changes across contexts happen through a public contract. A Host composes implementations but does not own business policy. Add negative tests for newly enforced architectural constraints.

**Synchronize:** Apply `DOC-UPDATE-RULES.md`. Update only documentation affected by the **actual change**. Update ownership/map/catalog atomically with contract and dependency edits, record ADRs for architectural choices, and include an impact note in the PR. Do not change metadata solely to make a violating dependency appear permitted.

**Verify:** Run focused behavior tests, then `python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json` or `--profile all --output artifacts/verification/full.json` on a .NET 10 machine. The runner executes six fixed Python checks; `all` also runs .NET restore/build/console smoke. Its nonzero exit and NOT_RUN results indicate missing evidence. CI stores evidence even on failure. `bash scripts/verify-all.sh` delegates to the same runner.

**Report:** Name owners, contracts, source/docs changed, test plan and files; link generated JSON evidence and report exact PASS/FAIL/NOT_RUN and missing categories. See `.github/pull_request_template.md`. For repeated agent failures, first add a negative regression case in `scripts/test_task_protocol.py`, then make the smallest correction and rerun checks.

## Scope-sensitive reading

- Small typo: read the file; apply only relevant checks.
- Domain change: read the owner's metadata + related rules/tests.
- New event or consumer: read publisher, all affected consumers, public contract catalog and migration guidance.
- New infrastructure/security pattern: read architectural ADRs, security/operations docs and relevant book chapters.
- New agent rule: update the skill, `AGENTS.md` as needed, index and skill-validator tests.

## Approval boundaries

Do not assume permission to deploy, publish packages, commit secrets, change production data, launch real financial transactions, or make destructive migrations. An architectural experiment that violates established ownership needs a documented human decision and explicit exception, with a time-bounded removal plan where justified.

## No hidden documentation autopilot

The skill cannot keep documentation updated while no agent is running. The agent must receive the repository and task, and the repo/skill must be loaded. CI can check metadata and static structure, but cannot infer every semantic inconsistency or read minds. The completion report must disclose unverified claims.
