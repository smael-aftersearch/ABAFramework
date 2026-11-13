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

## Opt-in per-host source load ceiling (incremental B queue change)

AbaCacheSourceLoadBudget is a singleton shared across request scopes.
ReadThroughQueryCache acquires a nonblocking permit **before** invoking
the authoritative query loader whenever UseAuthoritativeSource is chosen.
This applies both when Redis read fails and to a healthy Redis MISS
(because the subsequent cache write might fail). The configured
`Platform:Cache:OptInSourceLoadMaxConcurrent` is 8 by default and accepts
1..256; invalid startup values fail registration. Exhaustion raises
AbaCacheSourceLoadLimitExceededException; it **never** serves stale data,
suppresses authorization or pretends the source loaded. Source loader
faults and cancellation release permits in a finally-safe lease.
A failed Redis read is not retried for a write in the same request.

The ceiling bounds **simultaneous opted-in source invocations per host**.
It is deliberately not a rate-per-minute quota, distributed semaphore,
same-key cross-replica single-flight or a substitute for authorizing query
scope before cache access. A production DB can still be overloaded by
serial requests, cross-replica traffic or unbounded FailClosed cache misses.
The bounded, synthetic outage/miss tests are in CacheSourceBudgetTests;
real provider failure/restart and existing single-flight tests remain
independent CI gates.

## Bounded per-host in-flight cache-miss registrations (Queue B run 4)

The singleton `AbaCacheLoadGate` now caps **all registered callers** across
authorized scope/key partitions, including the active loader and callers
waiting behind a hot key. `Platform:Cache:MaxInFlightRequests` defaults
to **1024**, permits **1–4096**, and rejects invalid configuration at
registration. When the limit is reached,
`AbaCacheLoadGateSaturatedException` rejects a new request **before
a cache read or authoritative loader** and does not disclose tenant or
key metadata. Cancellation, a failed loader and lease disposal release
capacity; rejected requests never create another scoped entry.

This bounds in-process pending-request registrations, NOT remote clients,
serial DB query rate, Redis outage retries, multi-replica aggregate
source load or tenant authorization. The separate opt-in source-read
concurrency ceiling remains in effect. Tests check cross-tenant denial,
healthy retry after release, canceled-waiter cleanup and invalid config.
The new tests require the exact PR-head .NET CI before they count as
executed evidence.

## Open limits

This is **one-process** miss coalescing, not a distributed Redis
lock across multiple replicas. During an actual Redis outage, each
explicit source-fallback request can still execute after a prior permit
is released; no distributed source load budget or cross-replica stampede
protection exists, and not every loader can be safely retried.
The service does not authorize cache scopes, enforce PII retention,
guarantee cross-replica invalidation or replace the authoritative DB.
Do not use it to cache permissions, payments, accounting or
business invariants. Rate limits, quota distribution and
multi-node invalidation require owner-specific design.

CI tests: CacheSingleFlightTests, ReadThroughCacheTests,
AbaExternalInfrastructureTests.LiveRedisCollapsesConcurrentMissesAcrossRequestScopes
and isolated RedisRestartDrillTests, plus generated workflows.
