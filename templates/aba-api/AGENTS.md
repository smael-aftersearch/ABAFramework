# ABA API Starter — coding-agent rules

Read `docs/ai/INDEX.md`, `docs/architecture.md`, `docs/testing/TEST-STRATEGY.md`, and `.agents/skills/aba-engineering/SKILL.md` before changing behavior. The local Skill describes the execution loop; `AGENTS.md` remains the always-on guardrail.

- This repository is a **generic foundation**. Do not invent Enrollment, Academy, Payments, or any business domain.
- Identify the real owning bounded context before adding a business rule, data model, table or public contract. Record it in `metadata/contexts.json`.
- Keep every context's Domain independent of ASP.NET Core, EF Core, Redis, message brokers and other contexts' implementation projects.
- Cross-context communication uses versioned Contracts; do not directly write another context's database.
- Do not change an existing published V1 contract incompatibly; introduce V2 with compatibility and consumer tests.
- HTTP endpoints require authentication by default. Explicitly justify every `AllowAnonymous` endpoint. No secrets or unsigned JWT shortcuts.
- Keep OpenAPI generated from the actual endpoints. Documentation UI is Development only.
- Every material change requires a positive case, a negative case and invariant checks, plus updates to relevant tests and documentation.
- Execute `dotnet test` and `dotnet build` after modifications; report PASS/FAIL/NOT_RUN truthfully.
- Never claim production ready without integration, security, migration, load and recovery tests.
