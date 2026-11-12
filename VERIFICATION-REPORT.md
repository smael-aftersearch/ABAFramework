# Verification report — source authoring environment

**Prepared:** 2026-11-12

| Check | Result | Evidence |
| --- | --- | --- |
| Target is .NET 10 | Static PASS | `Directory.Build.props` contains `net10.0`; `global.json` pins SDK 10.0.100 |
| `.csproj` reference paths exist | Static PASS | Python governance checks all referenced project paths |
| Agent Skills package | **VALIDATED, executed** | Official skill-creator quick validator; installable `skill.zip` packaged |
| AI documentation readiness | **PASS, executed** | `python3 scripts/ai_check.py check` verifies agent entrypoints, skill, map, docs, CI wiring |
| Agent negative tests | **PASS, executed** | `python3 scripts/test_ai_check.py`: missing indexed doc, malformed skill name, missing owner metadata rejected; task brief positive and unknown-owner tests passed |
| Explicit test policy | **DOCUMENTED; static checks PASS** | `docs/testing/TEST-STRATEGY.md` and `tests/README.md` distinguish existing, future and unexecuted tests; AI skill now requires success/negative/invariant test cases |
| Documentation update impact hints | **PASS, executed** | `python3 scripts/ai_check.py brief --task "change payment contract" --file samples/Academy/Payments.Contracts/PaymentContracts.cs` |
| Metadata bootstrap overwrite safety | **PASS, executed** | `python3 scripts/generate_metadata.py` refused overwriting existing architecture metadata without `--force` (expected exit 2) |
| ABA governance | **PASS, executed** | `python3 scripts/governance.py` |
| ABA negative tests | **PASS, executed** | `python3 scripts/test_governance.py`: clean accepted; illegal private dependency, missing ownership and unversioned contract each rejected |
| Machine-readable metadata | **PASS, executed** | Python JSON parser (YAML 1.2 subset), ownership, contract catalog, dependency graph checks |
| Basic C# delimiter/solution scan | **PASS, executed** | `python3 scripts/static_review.py`; NOT a compilation test |
| API spec parses | **PASS, executed** | Governance parses `openapi-v1.json` |
| `dotnet --info` | **NOT AVAILABLE** | `dotnet: command not found` |
| `dotnet restore/build` | **NOT RUN** | .NET SDK missing; external DNS unavailable for installing |
| `dotnet run --project tests/Aba.SmokeTests` | **NOT RUN** | .NET SDK missing; test code authored but unverified |
| ASP.NET API HTTP workflow | **NOT RUN** | Runtime absent |
| Docker Compose | **NOT RUN** | Docker command unavailable |
| PostgreSQL, Wolverine, Redis, OAuth integration | **NOT IMPLEMENTED** | Phase-1 scope deliberately limited; see `docs/NEXT-MILESTONES.md` |
| ZIP file integrity | Verified at packaging | See accompanying checksums in final handoff |

**AI instructions and static checks cannot guarantee that every coding agent will automatically load or obey the skill, or that all prose documentation is semantically current.** Actual agent setup and task-level review remain necessary.

**No claim of a successful C# compile or end-to-end execution is made.** The next agent/developer must run .NET commands, fix any issues and promote the project to the next milestone.

## 2026-11-12 — AI Skill vNext: task-aware QA protocol

The repository-local ABA Engineering Skill was extended with an executable
`task_protocol.py plan` test-impact heuristic and a fixed-command `verify`
runner. These are test selection and execution-evidence tools; neither one
confirms a business owner or invents missing .NET tests.

| New verification | Actual observation | Evidence |
| --- | --- | --- |
| ABA skill-creator format | PASS | Skill package validated and repackaged from the updated full directory |
| Task plan selection | PASS | Refund/Outbox test suggestions, unknown-owner and API/security regressions |
| Task protocol regression tests | PASS | `python3 scripts/test_task_protocol.py`: 9 tests, including intentional failures |
| AI knowledge/CI wiring negative tests | PASS | `python3 scripts/test_ai_check.py` rejects deleted task runner and CI bypass |
| Fixed QA runner — static profile | **6 PASS, 0 FAIL, 0 NOT_RUN** | `docs/verification/2026-11-12-static.json` |
| Fixed QA runner — full profile | **6 PASS, 0 FAIL, 3 NOT_RUN** | `docs/verification/2026-11-12-full.json` |
| .NET restore/build/console smoke | **NOT_RUN** | .NET SDK unavailable here; full runner exits code 2 rather than reporting success |
| Actual CI workflow execution | **NOT_RUN** | YAML authored; a GitHub Actions execution has not been observed |
| .NET xUnit/real database/broker/Redis tests | **NOT IMPLEMENTED** | Still later milestones; no coverage metric claimed |

The evidence records real return statuses of commands actually invoked in this
environment. Static checks cannot substitute for .NET compilation or prove
semantic correctness of a new feature.