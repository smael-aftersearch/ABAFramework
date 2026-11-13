# ADR-014: Opt-in Kafka transport

**Status:** Accepted for the generic technical starter; production topology and business use remain owner-approved.

## Context

The generic API starter supports Wolverine relational durability and an opt-in RabbitMQ adapter. Some products require Kafka topics, but the Platform must not create product message contracts, topic names, consumer groups, partitioning rules or business retry/dead-letter policy. Kafka is an additional transport decision and does not replace the existing local or RabbitMQ defaults.

## Decision

- Keep local durable queues as the default. Kafka requires `Platform:Messaging:Enabled=true`, `Platform:Messaging:Transport=Kafka` and a credential-free `ConnectionStrings:AbaKafka` list of `host:port` bootstrap servers.
- Keep Kafka disabled by default. The Platform configures Wolverine's Kafka adapter with producer idempotence enabled and `acks=all`; routes and listeners remain host-supplied through the existing configuration callback.
- Products route only their own explicitly versioned Contracts to topics they own or are authorized to use. State-changing listeners should use Wolverine's durable inbox and handlers must make effects idempotent. Delivery remains at-least-once.
- Development may use a local plaintext broker. Production requires `SaslSsl`, a nonempty username/password and one of `Plain`, `ScramSha256` or `ScramSha512`. Bootstrap addresses cannot contain credentials. Authentication values remain in protected configuration and validation messages never include them.
- Kafka topic and Wolverine relational-resource auto-provisioning is allowed only when explicitly enabled in Development. Production requires reviewed topic ACLs, topic settings, database resources, TLS trust and operational policies before startup.
- Do not choose product retry, dead-letter, topic retention, partitioning, consumer group, schema registry or cross-cluster failover behavior in the Platform. Kafka requeue is an inline retry in Wolverine; owners must select and verify an appropriate policy.
- Integration CI uses the official `apache/kafka:4.4.0` image as a disposable, single-node KRaft broker and runs duplicate-payload durable delivery against PostgreSQL and SQL Server. These synthetic probes are not production cluster certification.

## Consequences

The version-matched `WolverineFx.Kafka` dependency is shipped by the template while the transport stays disabled by default. A PostgreSQL or SQL Server message store remains mandatory. Kafka producer idempotence prevents some producer retries from appending duplicates but does not create end-to-end exactly-once business effects. Broker integration tests must pass on the exact change head before the generic transport capability is promoted to implemented. Production membership, ACLs, managed secrets, topic policy, failover objectives and business consumer recovery remain product-owner work.

## Verification

The starter has unit tests for bootstrap validation and secure configuration, plus integration cases that publish duplicate technical probes through Kafka with PostgreSQL and SQL Server and assert two deliveries but one effect. The integration workflow must pass before claiming those live broker cases as executed. Local verification requires a reachable Kafka service via `ABA_TEST_KAFKA`; absent service evidence is `NOT_RUN`.
