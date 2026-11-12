# ABA API Starter — coding-agent rules

Run `python3 tools/aba_task.py plan --task "<the request>" --file <affected/path>` before coding, then `python3 tools/aba_task.py verify --level static` and `--level build` after changes. For infrastructure changes run `--level full` against real services; never report missing services as PASS. Read `docs/ai/TASK-PROTOCOL.md`.

Read `docs/ai/INDEX.md`, `docs/architecture.md`, `docs/testing/TEST-STRATEGY.md`, and `.agents/skills/aba-engineering/SKILL.md` before changing behavior. The local Skill describes the execution loop; `AGENTS.md` remains the always-on guardrail.

- This repository is a **generic foundation**. Do not invent Enrollment, Academy, Payments, or any business domain.
- For a **new** real business context, use `python3 tools/new_context.py create --name ActualContext`, inspect generated ownership data, and run `python3 tools/new_context.py check`. Do not copy Academy or invent business rules just to fill a scaffold.
- Identify the real owning bounded context before adding a business rule, data model, table or public contract. Record it in `metadata/contexts.json`.
- Keep every context's Domain independent of ASP.NET Core, EF Core, Redis, message brokers and other contexts' implementation projects.
- Cross-context communication uses versioned Contracts; do not directly write another context's database.
- Do not change an existing published V1 contract incompatibly; introduce V2 with compatibility and consumer tests.
- When tenancy is enabled, extract tenant scope from a verified signed JWT claim via `IRequestTenant`; never trust a user-supplied tenant header. Context-owned EF Infrastructure rows should implement `ITenantOwnedRow` and use `AbaTenantDbContext` for read filters and SaveChanges guards, **plus** domain membership and database/bulk-SQL defenses. Context owners must still test their row isolation independently.
- Preserve signed JWT xUnit tests for a valid token and invalid signature/issuer/audience/expiration; never replace ASP.NET Core token verification with a test-only fake authorization handler in production.
- Authenticate before assigning per-user rate-limit partitions; never trust raw forwarded/tenant headers, and keep Health probes exempt. A per-instance limiter does not replace shared gateway quotas.
- HTTP endpoints require authentication by default. Explicitly justify every `AllowAnonymous` endpoint. No secrets or unsigned JWT shortcuts.
- Keep OpenAPI generated from the actual endpoints. Documentation UI is Development only.
- Cache outage bypass is explicit and only for non-authoritative queries after proper tenant authorization. Never use read-through cache fallback for security decisions, idempotency or business invariants.
- Keep the generated `.github/workflows/ci.yml` and manually triggered integration workflow. Do not bypass failures, disable tests or grant write token permissions to make a release pass.
- Every material change requires a positive case, a negative case and invariant checks, plus updates to relevant tests and documentation.
- For Platform/DB changes, test against **both** PostgreSQL and SQL Server plus Redis with `bash scripts/verify-platform-services.sh`. Do not confuse an offline provider-name test with an actual database transaction. Execute `dotnet test` and `dotnet build` after modifications; report PASS/FAIL/NOT_RUN truthfully.
- Keep generated Dockerfile multi-stage and the container runtime non-root. Never expose Development-compose ports publicly; test that Production rejects missing OIDC config with `bash scripts/verify-container-starter.sh`.
- Never claim production ready without integration, security, migration, load and recovery tests.

- Production database transport must validate server identity and TLS: PostgreSQL `SSL Mode=VerifyFull`; SQL Server `Encrypt=True/Strict;TrustServerCertificate=False`. Do not remove the fail-closed startup check to accommodate insecure services; local/test containers must run in Development.
