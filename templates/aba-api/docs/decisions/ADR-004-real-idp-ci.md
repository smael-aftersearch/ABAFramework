# ADR-004: Real OIDC identity-provider verification is separate from synthetic JWT tests

**Status:** Accepted, 2026-11-12

The generated API still has no user login UI or token issuer and must never generate its own production JWT signing key.

The CI system starts an isolated, temporary Keycloak development realm with a test-only user and password grant, obtains its OIDC issuer and JWKS, and verifies through a freshly generated .NET API that: signed JWT + correct audience + trusted tenant claim returns 204; absent/malformed token returns 401; wrong audience returns 401; and a valid token without the tenant claim returns 403. The test has explicitly opted-in HTTP metadata on localhost **only in Development**. Production startup continues requiring HTTPS identity provider discovery.

The disposable realm and user credentials are fixtures; they confer no production authority. Production clients should use authorization code with PKCE or backend client credentials as appropriate, with external role/membership management, key rotation and TLS. Real tenant database row-level protection and authorization policy mapping remain separately owned concerns.
