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

## Redis readiness probe cleanup under read failure (Queue B run 7)

The provider readiness check uses a fresh opaque `aba:health:<random>` key
with a ten-second TTL. After successfully writing the probe, it now attempts
to remove **only that probe key** even if the subsequent read times out or
returns the wrong value. Cleanup failure returns **Unhealthy**, not Healthy;
read timeout and mismatch remain Unhealthy. Cancellation propagates normally
and the short TTL limits abandoned probe keys if the health check is canceled.
No tenant-scoped query key is inspected or deleted by this readiness check.
`AbaRedisHealthCheckTests` covers a successful probe, read timeout,
readback mismatch, cleanup failure and preserved unrelated tenant data.
This is local Redis hygiene; it is not an inter-replica lock, durable
invalidation protocol, permission check or production topology approval.
These authored tests require exact PR-head CI for executed PASS evidence.

## Open limits

This is **one-process** miss coalescing, not a distributed Redis
lock across multiple replicas unless the opt-in coordination below is
enabled. Local mode remains the default and retains its prior behavior.
The service does not authorize cache scopes, enforce PII retention,
guarantee cross-replica invalidation or replace the authoritative DB.
Do not use it to cache permissions, payments, accounting or
business invariants. Multi-node invalidation and query authorization
remain owner-specific.

CI tests: CacheSingleFlightTests, ReadThroughCacheTests,
AbaExternalInfrastructureTests.LiveRedisCollapsesConcurrentMissesAcrossRequestScopes
and isolated RedisRestartDrillTests, plus generated workflows.

## Opt-in Redis coordination across replicas

`Platform:Cache:DistributedCoordination:Enabled=true` requires the Redis
provider, an explicit unique application namespace and Redis connection.
After a cache miss, the coordinator takes a token-owned, expiring
per-scoped-key lease, reserves one of the configured cluster-wide source
load slots, and re-reads the cache before running the loader. Leases renew
while a loader is active and use compare-token renewal/release; lock keys
contain only the already hashed scope/key. A shared Redis counter also
limits attempted source loads per configured fixed window. Settings are
opt-in and validated; Production requires `ssl=true`. The coordinator
uses its own long-lived Redis multiplexer because the ASP.NET distributed
cache owns and disposes its own connection. A persistent, nonsecret settings
fingerprint rejects replicas that reuse a namespace with different budgets;
changing budgets requires a new namespace. The
`aba-cache-coordination` readiness check probes this connection and verifies
the fingerprint when distributed coordination is enabled; it is healthy
without resolving Redis in the disabled default mode.

If Redis cannot grant/renew a lease or the shared source budget is
exhausted, the loader does not run. In distributed mode this is true even
when the caller selected `UseAuthoritativeSource`: without a shared permit,
the Platform fails closed instead of creating uncoordinated replica load.
The source callback receives a cancellation token linked to lease loss and
must honor cancellation. If a callback ignores cancellation, the Platform
checks the linked token again before returning or caching its result. The
unit suite includes a cancellation-blind loader that finishes after lease
loss and asserts that its value is neither returned nor cached. This cannot
undo side effects inside an ill-behaved callback; loader owners still need
to observe cancellation. This is load coordination, not a correctness lock,
tenant membership grant, business idempotency, cross-replica invalidation or
a promise that Redis failover preserves leases/counters. A Redis failover,
eviction policy, multi-region setup and callback cancellation behavior need
real provider-backed review before Production use.

`AbaDistributedCacheCoordinationIntegrationTests` exercises independent
service providers against one Redis server for same-key collapse, shared
concurrency and serial-load budgets, configuration-drift rejection, and
fail-closed behavior with an unreachable server. These tests are authored;
the live Redis cases require the exact current test head and a disposable
Redis service to run.

## Malformed cached query payload recovery (Queue B run 9)

A provider read can succeed but return unparseable JSON (for example,
after damaged or version-incompatible cached query data). The local
per-key single-flight lease still bounds concurrent callers.
`CacheOutagePolicy.FailClosed` propagates `JsonException` **without**
invoking a source loader or changing the bad entry. Only callers that
explicitly choose `UseAuthoritativeSource` for already-authorized,
non-authoritative queries can refresh the malformed entry. Unlike a
Redis network outage, an invalid JSON payload is not treated as an
unavailable cache: the successfully loaded source value is written back
to **only that scoped key** and subsequent reads become hits.

The host-local opt-in source budget continues to limit loads; cancellation
and loader errors still propagate, and a failed load cannot overwrite the
corrupt entry. No business authorization errors are swallowed.
`CacheSingleFlightTests` covers a successful scoped repair, strict denial,
pre-cancelled no-source/no-write behavior, preserved malformed evidence,
isolated neighboring tenant data, and released gate/budget invariants.
Review exact PR-head checks at
https://github.com/smael-aftersearch/ABAFramework/pull/29/checks .
This does not deliver distributed invalidation, query authorization,
data-schema migration or production cache topology approval.

## Cancellation-blind provider/source guard (Queue B final run 10)

Provider adapters and callers are asked to honor `CancellationToken`, but an
external cache driver or authorized query loader may complete even after
cancellation. `ReadThroughQueryCache` now checks the token **after** a
provider cache read, before returning a cached value, and **after** a source
load, before cache writes or returning an outage fallback. This prevents a
known post-cancellation result or write when a driver/callback ignores the
token; it does not promise to undo a cache write that has already begun.

`CacheSourceBudgetTests` uses a deliberately cancellation-blind in-memory
`IDistributedCache` to prove a normal authorized retry succeeds, negative
read/loader cancellation is observed, and no cancelled source result is
written to either tenant namespace. Per-host gate registrations and opt-in
source permits are released. The existing strict failure semantics remain.
This is host-local cancellation safety, **not** signed tenant membership,
distributed invalidation or an approved Production cache policy.
The technical source/test HEAD passed [CI 43391409289](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43391409289); review the final [PR #30 HEAD checks](https://github.com/smael-aftersearch/ABAFramework/pull/30/checks) independently before merge.
