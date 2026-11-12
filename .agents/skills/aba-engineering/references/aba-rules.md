# ABA authority rules: concise source guide

Use the bundled repository PDF `docs/reference/ABA_Adaptive_Business_Architecture_English_Edition.pdf` for source-grounded interpretation. The PDF is a book of principles, **not** a complete framework manual or a mandate to copy its example domains (PDF pp. 2, 34).

| Source | Engineering requirement | Repo evidence |
| --- | --- | --- |
| Ch. 1-5, 47 | Optimize for lower future cost of change; complexity must justify itself | ADRs and narrowly scoped abstractions |
| Ch. 6-9, Appendix B/C | Data, rule, decision, and contract each have one context owner; boundaries are authority boundaries | `_Docs/ownership.yaml`, `context-map.yaml` |
| Ch. 8, 38 | Public Contracts are explicitly versioned and owned | `*.Contracts`, `contract-catalog.yaml` |
| Ch. 11-14, 41-42 | DDD + Clean internals and modular monolith first; don't confuse project layers with ownership | Context projects, dependency graph |
| Ch. 15 | Apply CQRS/Event Sourcing when evidence warrants their operational complexity | Explicit ADR if chosen |
| Ch. 16-18 | Framework provides mechanics, contexts own business decisions; secure protocol separated from business Identity | Platform projects, Identity context |
| Ch. 17, 30-31, 50 | State+Outbox share transaction; Inbox guards duplicates; Payments handles callbacks idempotently | Messaging stores, handlers, tests |
| Ch. 19-24, 39-40, 53-55 | Governance must be executable; metadata + ADRs must guide AI; measure drift | `scripts/governance.py`, CI, agent files |
| Ch. 28-29, 49 | Posted journals immutable and balanced; correct via reversals | Accounting model/tests |
| Ch. 32-35, 51-52 | Enrollment decides activation; Reporting projects source events without owning original facts | Events, ownership maps, reporting projections |

**Conflicting sources:** do not assume an old document reflects current implementation. The repository's code tells you what currently exists; approved ADRs + ownership metadata describe intent; the PDF is the governing architecture philosophy. If these disagree, surface the conflict and avoid expanding it. A change requiring a new interpretation needs a new ADR and governance update.

**Examples from the book (do not copy blindly):** `PaymentCapturedV1` is published by Payments; Enrollment and Accounting decide their own reactions. Reporting updates a projection. Platform does not decide payment capture, enrollment activation, or financial posting.
