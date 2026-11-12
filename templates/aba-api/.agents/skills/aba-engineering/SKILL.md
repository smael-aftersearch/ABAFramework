---
name: aba-engineering
description: >
  Implement, test, review or document an ABA-aligned C#/.NET 10 API generated
  from this template. Use this skill for every new endpoint, bounded context,
  contract, security change, persistence or messaging integration, caching
  change, CI update, or repository architecture refactor. Enforce explicit
  ownership, pure Domain, versioned public contracts and truthful test evidence.
---

# ABA engineering workflow for this generated repository

## Source-of-truth discovery

1. Read `AGENTS.md`, `docs/ai/INDEX.md`, `docs/architecture.md` and `docs/testing/TEST-STRATEGY.md`.
2. Inspect `metadata/contexts.json`: it starts with no fictional business owners.
3. For new business behavior, derive its single owning context from the real requirements. Record the owner and public versioned contracts before adding dependencies. Ask for clarification only when ownership cannot be determined safely.
4. Read the affected code and existing tests before editing.

## Mandatory boundaries

- Domain code contains no ASP.NET Core, EF Core, Redis, Wolverine or another context implementation.
- Other contexts consume versioned contracts, not private models or tables.
- The API host is composition-only; business decisions belong to owning contexts.
- Do not silently modify public V1 contracts. Introduce V2 and compatibility tests when needed.
- For tenant-aware code, require a validated JWT tenant claim and context-owned tenant/row isolation; never use a raw tenant header as an authority. Optional claim-aware middleware is not sufficient to claim full tenant isolation.
- HTTP routes remain authenticated by default; only narrowly justified endpoints can be anonymous.
- No production secrets, fake payment side effects, or claims of unimplemented PostgreSQL, Redis, Wolverine or tenant isolation.

## Deliver the task

1. State the actual owner, acceptance criteria, positive test, negative test and invariant check.
2. Implement in the owning context and update public contracts where necessary.
3. Add relevant xUnit tests; for HTTP behavior use `WebApplicationFactory<Program>`.
4. Synchronize `metadata/contexts.json`, API documentation, ADRs and developer instructions when changed.
5. Execute `dotnet restore`, `dotnet build -c Release` and `dotnet test -c Release`; report exact PASS, FAIL or NOT_RUN (never invent results).
6. Complete with ownership impact, touched files, tests, security issues and unverified work.

**The starting API intentionally has no domain sample. Do not copy Academy or Enrollment unless the real product requires those bounded contexts.**
