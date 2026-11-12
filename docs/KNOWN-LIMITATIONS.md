# Known limitations and security warnings

1. **C# not compiled in authoring environment:** .NET 10 SDK is absent, network DNS unavailable. Source has been authored and checked by Python structural validation only. Do not interpret the ZIP as a verified build.
2. **No production authentication or authorization:** this is a DEVELOPMENT-ONLY demo. The host aborts on non-development environments. Never bind it to a public IP, put behind a public reverse proxy, or connect real user data.
3. **Single-process local persistence:** atomic JSON-file replacement is an educational approximation of a local transaction. Two processes sharing a data directory risk lost updates. There are no database transactions, isolation, pooling, migrations, or failover.
4. **Local event delivery only:** the dispatcher is in-process, not Wolverine or RabbitMQ. Dead letters are stored but there is no admin API/CLI for replay. Delivery has no external ordering or message schema registry.
5. **Fake payment gateway:** there is no real charge, settlement, refund, callback signature verification or provider-side idempotency/reconciliation. Never connect a real payment processor to the current implementation.
6. **Simplified Accounting:** illustrative balanced append-only postings only, no chart-of-accounts governance, taxes, fiscal period lifecycle, reversal commands, currency conversions or compliance guarantees.
7. **In-memory caching:** not Redis and does not share invalidation across hosts; no tenant isolation (single demo tenant only). Avoid production use.
8. **Static OpenAPI:** manually maintained, not generated from endpoints; drift is not yet automatically checked.
9. **Minimal tests:** dependency-free console smoke checks are authored but not run; production integration, security, concurrency, contract-compatibility and stress tests remain outstanding.
10. **Governance coverage:** Python checks metadata, project references and simple source patterns. It is not yet an IL analyzer and cannot prove the absence of all dynamic cross-context access or security vulnerabilities.
11. **No distributed transactional consistency:** handlers may partially succeed; replay suppression uses inbox IDs and domain guards. The demo is not a financial-grade distributed system.
12. **Potential performance constraints:** full-file read/serialize/write per operation, in-memory event polling and non-evicting expired cache entries until read/removal; intentionally simple for an initial slice.

13. **Waitlisted payments:** the illustrative flow captures before making the final capacity decision; a payment may be recorded for a waitlisted participant. A production system must reserve seats before capture or define safe void/refund/compensation rules.

## Agent Skills / documentation limitation (2026-11-12)

The repo includes agent navigation, a portable Skill and static policy tests. These do not guarantee every AI platform discovers or follows those instructions, and cannot infer all semantic documentation drift from source code. Skills only execute when invoked by a supported agent that has access to the repository. The doc index and metadata are maintained when making tasks; CI checks the structure but does not replace human review of business meaning.

The `scripts/generate_metadata.py` program is a **bootstrap seed writer** and must not be used as a live synchronizer: by default it refuses to overwrite existing metadata, and `--force` requires deliberate review.

## AI test planning / evidence runner limitations (2026-11-12)

The Skill's task planner uses keywords and changed-path hints. It **does not**
assign authoritative ownership, prove which tests cover a change, or create
missing xUnit/integration tests. The verification runner executes fixed local
checks and writes evidence, but it **does not** run in the background or
certify production readiness. In this environment six Python gates passed and
three .NET steps were NOT_RUN due to absent SDK. Check `VERIFICATION-REPORT.md`.