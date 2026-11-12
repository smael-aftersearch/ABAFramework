# Tests in this repository

Read `docs/testing/TEST-STRATEGY.md` before changing behavior. It defines the **current test inventory**, target layout, change-to-test matrix, and mandatory AI test-evidence protocol.

## Executable today (if requirements are installed)

- `Aba.SmokeTests/Program.cs`: a **console** smoke runner, not an xUnit test project; invoke `dotnet run --project tests/Aba.SmokeTests -c Release` with the .NET 10 SDK. It covers the fake-payment enrollment flow, waitlist, accounting invariants, local message delivery/replay and persistence. Build/runtime not verified in the original authoring environment.
- `../scripts/test_governance.py`: run with Python 3; deliberately introduces forbidden architecture states and verifies rejection.
- `../scripts/test_ai_check.py`: run with Python 3; validates that missing or incorrect AI documents are detected.
- `../scripts/governance.py`: run with Python 3; checks repository structure, metadata, dependency boundaries and contracts.
- `../scripts/task_protocol.py plan --task "..."`: suggests a scope-aware test checklist and related docs; it does not prove coverage or assign ownership.
- `../scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json`: records true PASS/FAIL/NOT_RUN for six Python gates.
- `../scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json`: adds .NET SDK, restore/build, console smoke; nonzero when .NET is missing.
- `../scripts/test_task_protocol.py`: regression/negative tests for the task planner and evidence runner.
- `../scripts/verify-all.sh`: complete verification entrypoint via the evidence runner; .NET required for all stages.

The target xUnit-style Unit/Integration/Contract/Architecture/E2E project layout in the policy is a **proposal**, not a set of existing directories or successful test runs. Do not invent success or coverage metrics.
