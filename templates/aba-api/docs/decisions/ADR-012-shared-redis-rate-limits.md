# ADR-012 — Opt-in shared Redis rate limits across API replicas

**Decision (2026-11-13):** The generic API adds an optional, application-namespaced
Redis fixed-window quota **in addition** to its existing per-process limiter.
No business membership or trusted gateway policy is inferred. The default is OFF.

- Validated JWT subject+tenant (or actual TCP peer for anonymous requests) is
  hashed before being sent to Redis. Client `X-Tenant-ID` and `X-Forwarded-For`
  do not determine the partition. A unique application namespace must be set.
- Redis server `TIME` and one atomic Lua `INCR`/`EXPIRE` operation protect
  the shared budget from lost updates and per-replica clock skew.
  A fixed window allows up to twice the quota across adjacent window boundaries.
- Outside Development, enabling the shared limiter requires `ssl=true` in
  the Redis connection string; an insecure opt-in configuration fails at
  startup before a request is accepted. Development-only plaintext Redis
  continues to support disposable CI and local tests. TLS alone does not
  prove certificate trust, authentication, tenant membership or ACL approval.
- Redis timeout/unavailability returns 503, never a permissive independent
  per-host fallback; quota exhaustion is 429. Liveness is exempt, readiness
  reports the shared Redis dependency when enabled.
- Production deployment needs authenticated/TLS Redis, ACL/network isolation,
  monitored capacity and an approved reverse-proxy trust policy. By default
  anonymous requests use the actual TCP peer (usually the gateway).
- A quota partition is not tenant membership, RBAC or SQL authorization.

**Evidence required:** real multi-replica HTTP test against disposable Redis
with concurrent requests, hostile headers, namespace isolation, 429/503
and liveness/readiness; config negative tests. Review exact CI result.

**Rollback:** Disabling `Platform:RateLimit:Shared:Enabled` restores local-only
quotas but loses cross-replica protection. Require an approved gateway
mitigation before such a change in production.
