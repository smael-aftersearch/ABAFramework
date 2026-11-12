# AI task protocol for generated ABA projects

Every AI agent works from **current code, actual ownership metadata, tests and documentation**, not from prior chat memory or a speculative architecture prompt. The generated project's `AGENTS.md` sets mandatory guardrails; `.agents/skills/aba-engineering/SKILL.md` gives the sequence.

## 1. Identify the context before coding

```bash
python3 tools/aba_task.py plan --task "Add an actual Inventory query" --file src/Modules/Inventory/Application/Query.cs
```

If there is no approved business owner yet, the planner reports `NEEDS_OWNER` and exits nonzero; it does not invent one. A plan lists exact documents, owners, and the required **success / negative / invariant** checks. It is not evidence of implementation.

## 2. Build only the owner's module

Update the context's `_Docs/ownership.yaml`, `context-map.yaml`, `ai-rules.yaml`, test cases and any public versioned Contracts. Never import another context's private implementation. If new ownership is actually needed, use `python3 tools/new_context.py create --name ActualContext`. Technical infrastructure belongs in the Platform library, not arbitrary business contexts.

## 3. Verify with truthful, machine-readable evidence

```bash
python3 tools/aba_task.py verify --level static --output artifacts/verification/static.json
python3 tools/aba_task.py verify --level build --output artifacts/verification/build.json
```

Static verification checks executable context governance. Build verification restores and builds the generated .NET solution and runs xUnit. For persistence, messaging and cache changes, configure *all three* real external connections and run:

```bash
python3 tools/aba_task.py verify --level full --output artifacts/verification/full.json
```

The `full` level **fails closed** with `NOT_RUN` if the .NET SDK or any real-service connection is unavailable; no runner treats missing evidence as PASS. Commands are fixed arrays (no shell interpolation, no arbitrary user-supplied execution). Default xUnit does not require PostgreSQL, SQL Server or Redis.

## 4. Close the change with evidence

Include: owning bounded context; exact changed files and contract versions; accepted ADRs and metadata updates; PASS/FAIL/NOT_RUN tests (and paths to evidence); migration and rollback risks; remaining security/production limitations. Do not call a generated scaffold or successful smoke test fully production-ready.

This deterministic script assists AI but does not automatically infer a business owner from English task keywords, implement features or configure external IdP/DB credentials. Humans/agents must still derive authorized business decisions from actual requirements.

## Redaction and evidence privacy

Verification output tails redact configured PostgreSQL, SQL Server, Redis and client-secret environment values and common `Password=` fragments before writing JSON. Never copy full environment variables into task descriptions or `--output`; treat CI artifacts as potentially visible to colleagues. The test suite includes a synthetic secret-leak regression.
