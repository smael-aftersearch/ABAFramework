## Change summary and acceptance criteria

Describe the behavior changed, including at least one important failure/negative case.

## ABA architecture impact

- Owning bounded context(s), data/rule/decision owner:
- Published/consumed contracts and compatibility impact:
- New/removed dependencies (only public Contracts across contexts):
- Security, consistency, idempotency, migration or rollback impact:

## Agent-readable documentation changes

- Context ownership/context-map/ai-rules:
- Contract catalog, ADRs, `evolution.md`, OpenAPI/README as applicable:
- If no doc update, why it was not required:

## Required test plan and evidence

- Tests added/changed, paths and owner-context responsibility:
- Acceptance cases: happy path / negative case / invariant:
- New infrastructure, API, security, contract or regression tests needed:
- Categories not covered and explicit reason:

## Verification evidence

- Task-aware test plan: `python3 scripts/task_protocol.py plan --task "..." --file "..."`
- Recorded evidence (CI artifact or local `artifacts/verification/*.json`):
- `python3 scripts/test_task_protocol.py`:
- Gate summary (`python3 scripts/task_protocol.py verify --profile static|all`):
- Any NOT_RUN, uncovered category, missing xUnit/real DB/broker/auth test and reason:

- `python3 scripts/ai_check.py check`:
- `python3 scripts/governance.py`:
- `python3 scripts/test_ai_check.py` and `scripts/test_governance.py`:
- `.NET build`, C# smoke (`dotnet run --project tests/Aba.SmokeTests -c Release`), and relevant unit/integration tests:
- Tests not run (environment constraints) and remaining risks: