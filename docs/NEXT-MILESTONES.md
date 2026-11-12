# Work remaining to reach the requested full framework

The original requirement is in `docs/reference/TARGET-SPECIFICATION.md`. The phase-1 source demonstrates an initial ABA vertical slice and is intentionally incomplete.

## Milestone 2 — verified build + PostgreSQL / EF Core 10

1. Execute actual .NET 10 restore/build and the console smoke suite; resolve any compiler/runtime defects.
2. Introduce one PostgreSQL schema and context-owned `DbContext` per business context, complete EF Core migrations, explicit concurrency handling, and SQL transaction tests.
3. Replace JSON-file atomics with a real relational transaction writing business state and the Outbox together. Preserve `IOutboxSource` semantics or adapt interfaces with an ADR.
4. Test database retries, crashes between commit/publication, simultaneous requests, and cold-start migration behavior with Testcontainers.

**Gate:** a clean PostgreSQL instance supports the same API flow and restart recovery without local JSON state files.

## Milestone 3 — Wolverine, Redis and observability

1. Verify current WolverineFx stable APIs and .NET 10/EF Core integration against official documentation.
2. Use Wolverine durable Inbox/Outbox (and compatible broker transport where justified), retry/error queues, correlation IDs, and consumer-level idempotency.
3. Replace local `IReadCache` implementation with Redis-backed or hybrid caching; define cache invalidation and tenant-safe keys.
4. Add OpenTelemetry traces/metrics/log correlation, health probes and structured operational dashboards.

**Gate:** loss/retry/duplicate tests pass across independently restarted host and infrastructure containers.

## Milestone 4 — secure, production-aware API

1. Add Keycloak or another standards-based OIDC identity provider, OAuth 2.0, bearer-token validation and policy authorization.
2. Define tenant boundaries and isolate all owned storage and cache keys.
3. Add ASP.NET-generated OpenAPI, API compatibility testing, validation/ProblemDetails, throttling, structured audit logs, and secure configuration.
4. Require authentication/authorization end-to-end and remove the development-only production guard only after explicit security review.

**Gate:** documented threat-model checks, invalid-token tests and tenant-isolation tests pass.

## Milestone 5 — packaging and developer workflow

1. Publish reusable framework NuGet packages and at least one `dotnet new` template.
2. Expand governance with contract compatibility against a baseline, IL-level dependency checks/analyzers, API drift detection, and automatic dependency graph output.
3. Add professional xUnit projects, CI restore/build/test/vulnerability scanning, template instantiation tests, and migration guide.
4. Extend sample to realistic provider callback validation, refunds/reconciliation, append-only correction journals, notification adapters and compensation policies (only when justified by ABA).

**Gate:** an engineer can generate and build a new application, add a bounded context, and deploy locally by following the README only.

## Agent/documentation automation hardening (future)

The first document-driven ABA Skill has been added and verified with Python tests. Further useful work is to (1) compare changed contracts against a baseline and detect breaking API/event changes, (2) validate source-to-document references and freshness on PR diffs, (3) test the actual skill against multiple real coding tasks and score false negatives, and (4) optionally surface documentation/ownership health in a dashboard. These are **not yet implemented**. Avoid pretending a static document map can prove semantic correctness.