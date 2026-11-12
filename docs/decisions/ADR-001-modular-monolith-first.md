# ADR-001: Begin as a governed modular monolith

**Status:** Accepted for Phase 1

**Context:** ABA Chapters 6–9 and 13–14 prefer explicit ownership and versioned boundaries over premature distribution. We need an immediately useful sample with minimal operational dependencies.

**Decision:** One ASP.NET deployable host wires five owned business contexts. Contexts expose only versioned Contracts for other contexts. A Python governance tool rejects private cross-context project dependencies; the Host is composition root.

**Alternatives:** Microservices (too much operational cost before ownership pressure exists); shared Domain library (prohibited by ABA).

**Tradeoff:** Context implementations still coexist in the same process. Some infrastructure concerns remain coupled at deployment time.

**Consequences:** Extracting a context later is facilitated by stable Contracts and isolated storage; independent deployment is not available today.
