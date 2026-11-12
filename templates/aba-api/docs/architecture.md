# Domain-neutral ABA starter

This is a **starter** for a new API product, not a sample academy/enrollment domain. The API host is the composition root, not a business owner.

## Current implemented baseline

- .NET 10 minimal API
- generated OpenAPI and Development-only interactive Scalar API reference
- liveness health check, correlation ID response header, Problem Details exception handling
- built-in fixed-window global request rate limiting (per process and remote IP; gateway controls are still needed at scale)
- fail-closed non-Development OIDC/JWT configuration and authorization fallback policy
- xUnit integration and security-configuration tests
- AI agent and test documentation

## Adding your first real context

Name the context using actual business language. Record its owning data, rules, decisions and versioned contracts in `metadata/contexts.json`. Then create appropriate Domain, Application, Infrastructure and Contracts projects, register their services in the Host, and add dependency-ownership tests. Do not create shared domain entities.

## Not delivered by this starter yet

No PostgreSQL/EF Core migrations, Wolverine Outbox/Inbox, Redis distributed caching, tenant isolation, durable jobs, provider integration, deployment pipeline or production hardening. These remain separate platform milestones and must not be asserted as installed.

## Security

The host does not ship a JWT signing key or an identity provider. For non-Development startup, provide `Authentication:Authority` (a trusted HTTPS OIDC issuer URL) and `Authentication:Audience`. Signature, issuer and audience validation then use the JWT bearer middleware. Verify provider-specific configuration and authorization policies before exposing public APIs. All non-explicitly-anonymous routes require authentication.
