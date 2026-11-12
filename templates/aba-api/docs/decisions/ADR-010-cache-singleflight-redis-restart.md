# ADR-010 — Host-wide single-flight cache misses and disposable Redis restart drill

**Status:** Technical implementation, Production multi-node gates open.
**Owner:** Platform owns only cache coordination and the provider adapter.
Business contexts authorize cache scope and distinguish query data from
authoritative rules. No business contract or domain entity is introduced.

## Design

ReadThroughQueryCache now gets a singleton AbaCacheLoadGate from DI.
It computes the already-existing opaque ScopedJsonCache.KeyFor(scope,key)
before acquiring a short-lived, per-key SemaphoreSlim lease. The cache
is read **inside** this gate and its result checked again. While Redis
or in-memory caching is healthy, 24 overlapping HTTP request scopes
for the same tenant+key invoke the authoritative loader once and all
receive the cached result. Different scopes or keys do not serialize.

Each gate has a reference count including both the executing caller
and queued callers. The last caller deletes and disposes the entry,
preventing a retained dictionary of every key ever read. Cancellation
of a waiting caller decrements its reference correctly. A failing
source loader releases the gate, and the next request can retry.
No business idempotency or tenant membership decision is inferred.

Previous read-through code confused an absent cache entry with the
valid CLR default of a value type (0/false) or JSON null. The new
ScopedJsonCache.TryGetAsync<T> returns (Found,Value), so an actual
cache miss is different from a legitimate cached default or null.
The existing GetAsync<T> remains source-compatible.

## Real provider restart failure contract

The isolated RedisRestartDrill never stops the Redis service shared
with the SQL/Wolverine integration job. It runs against its own
redis:7-alpine GitHub Actions service. A test may issue Docker stop
and start only after ALL of the following checks pass: CI=true,
ABA_ENABLE_REDIS_RESTART_DRILL=yes, an exact 64-hex Docker container
ID from job.services.redis.id, one parsed loopback Redis endpoint
on port 6379 and Docker's live .Config.Image=redis:7-alpine.
A separate negative test rejects remote/multi-endpoint,
non-standard port, non-opt-in CI and unsafe container IDs.

After priming a real StackExchange.Redis client, the test actually
stops that Redis process: default FailClosed MUST fail and must NOT
execute the authoritative loader; the explicitly permitted
UseAuthoritativeSource policy MUST load fresh data. The same
client instance must reconnect after the exact CI service is
restarted and pass cache read/write, readiness, hit and tenant
isolation assertions. A finally block restores the service after
assertion failures too. Only non-authoritative already-authorized
read-only queries may select this fallback.

## Open limits

This is **one-process** miss coalescing, not a distributed Redis
lock across multiple replicas. During an actual Redis outage, each
explicit source-fallback request can still execute independently;
there is no distributed source load budget or stampede protection
for downed Redis, and not every loader can be safely retried.
The service does not authorize cache scopes, enforce PII retention,
guarantee cross-replica invalidation or replace the authoritative DB.
Do not use it to cache permissions, payments, accounting or
business invariants. Rate limits, quota distribution and
multi-node invalidation require owner-specific design.

CI tests: CacheSingleFlightTests, ReadThroughCacheTests,
AbaExternalInfrastructureTests.LiveRedisCollapsesConcurrentMissesAcrossRequestScopes
and isolated RedisRestartDrillTests, plus generated workflows.
