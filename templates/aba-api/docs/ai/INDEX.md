# AI knowledge index — generated ABA API

Read these sources progressively before changing behavior:

1. `AGENTS.md` — repository-wide architectural and security guardrails.
2. `.agents/skills/aba-engineering/SKILL.md` — step-by-step AI implementation workflow.
3. `docs/architecture.md` — technical structure and bounded-context boundaries.
4. `metadata/contexts.json` — context catalog and explicitly owned data, rules, decisions and contracts.
5. `docs/testing/TEST-STRATEGY.md` — positive, negative, invariant and real-service tests.
6. `docs/platform-infrastructure.md` and `docs/decisions/ADR-*.md` — dual SQL providers, Wolverine/Outbox, Redis, telemetry and security decisions.
7. `docs/context-generator.md` — when adding an **actual** business context, use `python3 tools/new_context.py create --name ActualContext`, then `python3 tools/new_context.py check`. Never fabricate a business domain.
8. `src/Modules/<Context>/_Docs/ownership.yaml`, `context-map.yaml`, `ai-rules.yaml`, `decisions.md`, `evolution.md` — the owner-local source of truth after a context exists.

Before committing code, run `python3 tools/new_context.py check`, `dotnet build -c Release` and `dotnet test -c Release`. For changes to PostgreSQL, SQL Server, Wolverine or Redis, run the separate fail-closed integration tests against real services; for deployment changes, validate the generated Docker image and NuGet package. Report PASS/FAIL/NOT_RUN with exact evidence.

This template is intentionally **domain-neutral**; no Academy or Enrollment behavior is present. The AI instructions guide work but cannot substitute for independent tests or verified ownership.

For RLS/raw-SQL or release-related work also read
`docs/decisions/ADR-005-tenant-row-security.md` and
`docs/platform-infrastructure.md`, plus the actual generated
`.github/workflows/ci.yml` image vulnerability job. The upstream
template artifact attestation does not automatically sign new products.

For sensitive audit, signing keys or failed durable messages, read
docs/decisions/ADR-006-audit-ledger.md,
docs/decisions/ADR-007-wolverine-deadletter-operator.md and
docs/operations/audit-deadletters.md, then relevant tests. Do not
invent auditable business decisions or grant wildcard message replay.
