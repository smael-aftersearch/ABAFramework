# ADR-003: Domain-neutral API template is the default entrypoint

**Status:** Accepted, 2026-11-12

## Problem

The original Academy paid-enrollment example had been mistaken for the requested reusable API framework. Business-specific contexts in a sample must not appear in a newly generated product.

## Decision

Expose a `dotnet new aba-api` template as the **default** entrypoint. The generated .NET 10 solution is self-contained and contains no example business contexts. It includes a minimal technical API host, generated OpenAPI, Development-only Scalar UI, a fail-closed non-development JWT configuration, health/correlation/rate-limit/Problem Details behavior, real xUnit integration tests, AI agent instructions and an empty ownership catalog.

Keep `samples/Academy` as an optional reference application only. Do not treat the Academy contexts as mandatory components.

## Consequences

- New products have an honest empty business boundary, and developers choose actual owners.
- Platform adapters for PostgreSQL/EF Core, Redis and Wolverine remain explicit future integrations. The template must not claim they are already production-ready.
- CI must `dotnet new` a fresh named project, restore/build/test and verify real HTTP/Scalar/OpenAPI behavior.
- Changes to this template update the generated agent documentation and tests together.
