# Evidence, handoffs, and documentation drift

## Completion checklist for material tasks

- Did the agent identify the owning context and explain why?
- Did it read that context's ownership/map/AI-rules and only the necessary neighboring documents?
- Are new dependencies only to published Contracts?
- Are Contract versions and consumers explicitly recorded?
- Were business invariants, idempotency, security and negative cases tested?
- Were generated/maintained docs updated based on **actual** code changes?
- Did CI and governance run? If a required tool was unavailable, is the limitation explicit?

## Evidence classification

- **Verified:** observed passing command with a concrete exit status or cited test report.
- **Authored, unverified:** code/test written but not executed (e.g. missing .NET SDK).
- **Planned:** not yet implemented; must not appear as a completed feature.
- **Failed:** include reproduction and proposed fix, not an optimistic summary.

## Handling disagreements

If the PDF, metadata, ADR, API spec, and code disagree:

1. Capture paths and precise conflicting claims.
2. Do not silently choose the more convenient answer.
3. For low-risk documentation typos, correct facts verified by code/tests.
4. For changes in ownership, public contract compatibility, security or persistence, record a new ADR and obtain a decision if the intent is not determined.
5. Update machine-readable metadata, affected tests, and doc index in the same change.
6. Demonstrate that governance detects a representative invalid state.

## Copyable task report

```markdown
### ABA architecture impact
- Request and acceptance criteria:
- Owning context(s):
- Data/rule/decision owner:
- Public contracts and consumers:
- Allowed/removed dependencies:
- Security and data-migration impact:
- Documentation + metadata + ADRs updated:

### Evidence
- Commands executed and their results:
- Not run (environment constraints):
- Remaining risks / next safe step:
```
