# Aba.Starter — a domain-neutral ABA API

A reusable .NET 10 starting point. **No example business domain is included.**

## Requirements

- .NET 10 SDK.
- NuGet access for ASP.NET Core OpenAPI, Scalar, JWT bearer and xUnit packages.

## Start locally

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

## Security

All endpoints are authenticated by default (fallback authorization policy). `/health/live` and Development-only documentation are anonymous; the system status endpoint is also anonymous **only in Development**. In other environments, the host **refuses to start without a trusted HTTPS OIDC Authority and Audience**. It has no bundled signing key. Never treat Development mode as public-hosting configuration.

## Architecture

Start with `AGENTS.md` and `docs/ai/INDEX.md`. Add bounded contexts **only for your actual business domains** and record ownership in `metadata/contexts.json`. Avoid premature shared Domain models. Implementations and integrations (EF Core, Redis, Wolverine) are deliberate follow-up milestones, not present in the generated project.
