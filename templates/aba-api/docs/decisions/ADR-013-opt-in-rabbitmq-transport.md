# ADR-013: Opt-in RabbitMQ transport

**Status:** Accepted for the generic technical starter; production topology and business use remain owner-approved.

## Context

The generic API starter already has Wolverine 6 relational durability using PostgreSQL or SQL Server. Products need a supported way to connect to an external broker without putting a business message, queue, authorization rule, or owner into the template. RabbitMQ is the first supported broker adapter; Kafka is not included.

## Decision

- Local durable queues remain the default. External messaging requires `Platform:Messaging:Enabled=true` and `Platform:Messaging:Transport=RabbitMQ` plus `ConnectionStrings:AbaRabbitMQ`.
- The Platform configures the RabbitMQ transport and its publisher confirmations. Confirmation tracking is enabled so a rejected publish keeps the relational outbox envelope available for retry.
- Products supply handler discovery and explicit message routes through the `AddAbaPlatform` Wolverine configuration callback. Use the owning context's versioned Contracts; do not route every message or add business contracts to the Platform.
- Listeners must use Wolverine's durable inbox when handling messages that affect durable state. Handlers remain at-least-once and owners must make effects idempotent.
- Wolverine database resources and RabbitMQ topology may auto-provision only in an explicitly opted-in Development environment. Production keeps automatic creation off and requires reviewed database resources, exchanges/queues/bindings, credentials, TLS trust and operational retry/dead-letter policies.
- Production RabbitMQ settings require an `amqps://` URI with a non-guest username and password. The built-in `guest` username is rejected case-insensitively even if its default password was changed; Operations still owns creation and least-privilege configuration of the product-specific principal. The URI is treated as a secret and is never included in validation errors or verification artifacts.
- CI's outage drill may pause only its exact disposable, labeled `rabbitmq:4.4.0-management-alpine` service and always resumes it in cleanup.

## Consequences

The generated project carries the version-matched Wolverine RabbitMQ package while keeping its broker connection and transport disabled by default. A relational database remains required for Wolverine durability. RabbitMQ availability does not turn the outbox into exactly-once delivery, approve a business schema or retry policy, or establish Production readiness. At the time of this decision, Kafka was not included; the later [ADR-014](ADR-014-opt-in-kafka-transport.md) records its separate opt-in adapter decision.

## Verification

The template includes configuration rejection tests, real RabbitMQ duplicate-payload tests against both supported SQL providers, and an isolated broker pause/recovery drill. These prove only the technical probes and exact disposable CI fixture. The exact-head CI run must pass before the capability is marked implemented.

## Fail-closed raw endpoint parsing (technical hardening)

The RabbitMQ adapter now rejects raw control characters in its supplied
amqp/amqps connection URI *before* `Uri.TryCreate`; URI parsing must not
silently normalize newline, carriage-return, TAB or other control bytes
from a secret-bearing address. A generic non-echoing error is returned.
Valid Development URIs and Production non-guest AMQPS credentials remain
supported. This is configuration hygiene, not certificate validation,
approved broker ACLs or Production topology authorization.

Generated API xUnit tests include positive Production AMQPS, negative
control-bearing URI fixtures in Development and Production, and unchanged
default-disabled behavior. CI on the exact change HEAD must PASS before
this is considered executed evidence.
