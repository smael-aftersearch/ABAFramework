# ADR-002 — Signed bearer token verification in the default suite

Status: Accepted | Date: 2026-11-12

## Decision

The domain-neutral starter ships no signing secret and no fake authentication middleware. Production requires an HTTPS OIDC authority and audience. The default xUnit suite uses a disposable **test-only** symmetric signing key injected into ASP.NET Core's actual `JwtBearerOptions` through WebApplicationFactory. This exercises the real signature, issuer, audience and lifetime validators without a network dependency. It tests positive and negative tokens. It does not replace testing a real externally hosted identity provider, external JWT key rotation, federation, organization membership or tenant data isolation.

The integration project covers real databases/Redis/Wolverine. It is visible in the solution under `/Tests/`, but the default test command runs only service-free unit-test projects. Integration tests fail closed when required connection strings are missing.

## Consequences

Any change to bearer middleware must preserve signed-valid and signed-invalid tests, and changes to external adapters must preserve the separate real-service test gate. Never publish or use the disposable test key in actual runtime configuration.
