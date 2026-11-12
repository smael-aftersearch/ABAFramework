# ABA compliance matrix: Phase 1 evidence

> Evidence is **code presence and executed Python governance**, except where explicitly marked **not verified in runtime**. All rows should be rechecked after `dotnet build` and the C# smoke suite run in an environment with the .NET 10 SDK. The authoritative book is `docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf`.

| ABA rule | Book references | Implementation evidence | Enforcement / status |
| --- | --- | --- | --- |
| Reduce cost of future change | Ch. 1–5, 47 | 5 isolated contexts; reusable technical framework; ADR-001 | Documented; economics not yet quantitatively measured |
| Exactly one business owner | Ch. 6–9; Appendix B | `*/_Docs/ownership.yaml`, `*/_Docs/context-map.yaml` | `ABA004`, `ABA011` checks pass |
| Bounded contexts are authority boundaries | Ch. 7, 11–14 | `samples/Academy/{Identity,Enrollment,Payments,Accounting,Reporting}` | `ABA001` dependency checks pass |
| Only Contracts are public | Ch. 8; Ch. 38 | Five `*.Contracts` projects; public DTO/events `V1` | `ABA001`, `ABA003`, `ABA011` checks pass |
| Domain independent of technology | Ch. 11–12, 42 | `Domain/` files within each context; `Aba.Foundation` | `ABA002` source checks pass |
| Platform owns technical capabilities only | Ch. 16–18 | `src/Framework/` local messaging, foundation, cache | `ABA009` checks pass |
| Transactional Outbox/Inbox | Ch. 17 | `DurableContextStore<TState>`, `OutboxDispatcher`, handlers | Local atomic-file algorithm authored; C# runtime **not verified** |
| Payment idempotency | Ch. 30–31, 50 | `PaymentsService`, `PaymentAggregate`, consumer inbox | Console smoke suite authored; **not run** |
| Accounting immutability | Ch. 28–29, 49 | `Journal.PostPayment`, `PaymentToJournalHandler` | `ABA010` source check passes; C# behavior **not run** |
| Enrollment business-owned capacity | Ch. 32–33, 51 | `EnrollmentAggregate`, `PaymentCapturedEnrollmentHandler` | Smoke test authored; **not run** |
| Reporting is a projection | Ch. 34–35, 52 | `ReportingProjectionHandler`, `ReportingData` | No direct source-context table reads; runtime **not verified** |
| Executable governance | Ch. 19–20, 39, 54 | `scripts/governance.py`, negative tests, CI workflow | **Executed:** clean + three intentional violations |
| ADRs + AI-readable metadata | Ch. 22–24, 53, 55; Appendix E | `_Docs/`, `AGENTS.md`, `contract-catalog.yaml`, ADRs | Metadata validity verified by Python |
| Architecture telemetry and score | Ch. 21, 40 | Governance status as first objectively measured signal | Full telemetry/health score **not yet implemented** |
| Professional API contracts | Ch. 8 and user requirement | `Host/Program.cs`, `openapi-v1.json` | API spec parses; HTTP runtime **not verified** |
| OAuth/OIDC and Redis | Ch. 16, 18 and user requirement | Planned in `NEXT-MILESTONES.md` | **Not implemented**; explicitly excluded from preview |
| .NET reference and repo template | Ch. 41–42 | `.slnx`, 15 `net10.0` project files, workflow | Project references checked; .NET compile **not run** |

## AI agent documentation compliance — new phase-1 extension

| Agent capability | Implementation | Automated evidence |
| --- | --- | --- |
| Persistent repository behavior | `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `.github/copilot-instructions.md` | `AI001` and `AI004` checks |
| Reusable installable skill | `.agents/skills/aba-engineering/SKILL.md`, `agents/openai.yaml`, references/scripts | Skill creator validator; `AI002/AI003` |
| Task-specific document discovery | `docs/ai/knowledge-map.json`, `scripts/ai_check.py brief` | Positive and unknown-owner tests |
| Task-aware QA planning | `scripts/task_protocol.py plan` and `references/testing-and-evidence.md` | Positive and negative protocol tests; does not certify coverage |
| Machine-recorded QA evidence | `scripts/task_protocol.py verify`, CI artifact upload | PASS/FAIL/NOT_RUN per check; .NET runtime remains unverified locally |
| Agent-readable documentation integrity | Index, context `_Docs`, catalog and AI policy validator | `AI005`–`AI009`, three seeded negative tests |
| Code-and-doc synchronization instructions | `docs/ai/CHANGE-PROTOCOL.md`, `DOC-UPDATE-RULES.md`, PR template | Process specified; semantic freshness still requires review |
| Resilience to destructive metadata regeneration | `scripts/generate_metadata.py` now refuses silent overwrites | Bootstrap guard checked; deliberate `--force` remains dangerous |

This tooling does **not** make agent compliance guaranteed or perform autonomous background document updates. It provides repeatable task instructions plus an independent structural safety net.

The book explicitly warns that sophisticated patterns must **pay rent** (Ch. 4 and 15). This first phase deliberately does not falsely claim that local file messaging is Wolverine, or that local memory caching is Redis. Production-ready infrastructure is a separate accepted milestone.