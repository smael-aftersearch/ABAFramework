# Task-aware QA and evidence: operator reference

A reading suggestion is not an ownership decision. A generated test plan is not an executed test. The agent must inspect the changed code, owning context metadata, consumers and actual test implementations.

## Before implementation

From the ABA repository root:

```bash
python3 scripts/ai_check.py brief --task "Add an idempotent refund" --file samples/Academy/Payments.Contracts/PaymentContracts.cs
python3 scripts/task_protocol.py plan --task "Add an idempotent refund" --file samples/Academy/Payments.Contracts/PaymentContracts.cs
```

Use the first command to discover reading material, and the second to generate **candidate** owner(s), test categories and examples. Confirm actual authority from `ownership.yaml`, `context-map.yaml`, ADRs, Contracts and source. If multiple contexts appear, separate what each one owns; do not assign the entire task to all of them. An unknown owner is a blocker for changes that would create a new business boundary.

Prepare a small acceptance table before coding:

| Case | Behavior | Test path or planned file | Status |
| --- | --- | --- | --- |
| Happy path | Expected successful change | real path, or `PLANNED` | `AUTHORED` / `RUN` / `PLANNED` |
| Negative | Invalid transition / duplicate / unauthorized access | real path, or `PLANNED` | same |
| Invariant | Business rule remains true under edge cases | real path, or `PLANNED` | same |

Do not invent test paths. Before replacing a fixture, preserve assertions and demonstrate that the replacement runs.

## Verification with recorded evidence

```bash
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json --markdown artifacts/verification/static.md
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json --markdown artifacts/verification/full.md
```

- `static` runs AI readiness, agent negative tests, task-protocol tests, architecture governance, governance negative tests, and static code scan.
- `all` adds `.NET 10 SDK` detection, restore, build and the existing Academy console smoke runner; it does **not** magically add xUnit or live PostgreSQL/Redis/Wolverine tests.
- The verifier runs a fixed set of trusted local commands without `shell=True`. It does not execute user-supplied command strings or mutate business data.
- The verifier produces a structured JSON evidence file and a human-readable Markdown summary. Statuses are **PASS**, **FAIL**, and **NOT_RUN**; omitted tests must be disclosed in the agent's final report.
- Exit code is `0` only when **every requested check** passed; `1` for any failed check; `2` for any not-run check (and no failure); `3` for invalid arguments or output paths.
- If the SDK or Docker is absent, runtime checks are **NOT_RUN**, never PASS; document the gap and give the exact commands needed on an appropriate host.
- Evidence artifacts under `artifacts/verification/` are ignored by Git by default. Commit only reviewed artifacts explicitly when required, never secrets.
- Treat the evidence file as command execution evidence, not proof of behavioral coverage or documentation semantic correctness. This script does not certify production readiness.

## Task completion format

```text
Task: <request>
Owning context/rule: <confirmed from metadata + source>
Changes: <source, tests, docs>
Public contracts and consumers: <versions and impact>
Acceptance cases: <success, negative, invariant> with exact test paths
Evidence: <JSON path, exact PASS/FAIL/NOT_RUN for static and runtime gates>
Unverified categories: <explicitly named, e.g. xUnit, real DB, auth, load>
Next action: <smallest safe remaining work>
```

## Improve the Skill safely after failures

1. Capture a reproducible failure or missed classification (task, file paths, expected response).
2. Add a negative regression test to `scripts/test_task_protocol.py` or `scripts/test_ai_check.py` before modifying the planner/checker.
3. Update the smallest rule, knowledge map entry or task playbook that corrects the failure.
4. Run the negative tests, both validators, and static verification; require .NET runtime for C# source behavior claims.
5. Record the new rule and its effect in `docs/ai/CHANGE-PROTOCOL.md` and update `AGENTS.md` only for globally mandatory behavior.
6. Do not rewrite the ABA PDF, auto-approve ownership changes, hide failing tests, or claim the installed ChatGPT Skill updated merely because its repository copy changed.
