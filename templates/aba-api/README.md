# Aba.Starter — a domain-neutral ABA API

A reusable .NET 10 starting point. **No example business domain is included.**

## Requirements

- .NET 10 SDK.
- NuGet access for ASP.NET Core OpenAPI, Scalar, JWT bearer, xUnit, EF Core 10, PostgreSQL/SQL Server provider packages, Wolverine 6, Redis and OpenTelemetry (optional services remain off by default).

## Start locally

With Docker installed, the quickest launch is `docker compose up --build` in the generated project directory. The API and Scalar UI are bound to **localhost:5174 only**; no database or Redis is required unless explicitly enabled. The generated multi-stage Dockerfile uses the non-root .NET runtime user. The image is designed for trusted configurable deployments, but does **not** turn Development mode into a secure publicly exposed server.

You can also run the .NET SDK directly:

```bash
dotnet restore Aba.Starter.slnx
dotnet run --project src/Aba.Starter.Api
```

In a local Development launch profile, visit `http://127.0.0.1:5174/scalar` for an interactive API reference, `/openapi/v1.json` for the generated OpenAPI document, and `/health/live` for a liveness check.

If running from a shell that doesn't choose a launch profile, set `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://127.0.0.1:5174` explicitly.

## Test

```bash
dotnet build Aba.Starter.slnx -c Release
dotnet test Aba.Starter.slnx -c Release --no-build
```

To run **real external-service tests**, configure the three `ABA_TEST_*` connection strings and run `dotnet test tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj`. This suite is intentionally excluded from the default solution; it fails if an external connection is missing.

## Security

All endpoints are authenticated by default (fallback authorization policy). `/health/live` and Development-only documentation are anonymous; the system status endpoint is also anonymous **only in Development**. In other environments, the host **refuses to start without a trusted HTTPS OIDC Authority and Audience**. It has no bundled signing key. Never treat Development mode as public-hosting configuration.

## Architecture

Start with `AGENTS.md`, `docs/ai/INDEX.md` and the included `.agents/skills/aba-engineering/SKILL.md`. Add bounded contexts **only for your actual business domains** and record ownership in `metadata/contexts.json`. Avoid premature shared Domain models. EF Core 10 provider selection (PostgreSQL and SQL Server), optional Redis, and Wolverine relational durability are now implemented as technical adapters in the generated Platform project. They are disabled by default to preserve the zero-dependency first run; read `docs/platform-infrastructure.md` before enabling them. Domain migrations, production runbooks and real fault-injection/outbox integration tests remain future work.

## Optional real database, cache, messaging and telemetry

The `src/Aba.Starter.Platform` technical library supports **both** PostgreSQL and Microsoft SQL Server with EF Core 10. Enable exactly one provider per deployment by setting `Platform__Database__Provider=PostgreSql` or `SqlServer` and supply `ConnectionStrings__AbaDatabase` securely. EF Core migration is included for the technical idempotency table. Toggle `Platform__Cache__Provider=Redis` with `ConnectionStrings__AbaRedis`; enable Wolverine relational durability with `Platform__Messaging__Enabled=true`. Enable OpenTelemetry with `Platform__Observability__Enabled=true` and optional OTLP endpoint. The `Platform__Tenancy__Enabled=true` flag requires an authenticated tenant claim, but **full tenant data isolation is not implemented**.

Read [docs/platform-infrastructure.md](docs/platform-infrastructure.md) for configuration details and [docs/architecture.md](docs/architecture.md) for boundaries. The host remains domain-neutral; no Academy context is generated.

**Actual integration evidence:** [GitHub Actions real containers](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904) passed PostgreSQL and SQL Server EF migrations/idempotency, Redis and Wolverine technical message delivery for both engines. Do not infer production-level durability/security from those baseline tests.
