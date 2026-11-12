# Verification: domain-neutral ABA API starter (2026-11-12)

**Repository:** `smael-aftersearch/ABAFramework`, branch `main`
**Source commit:** `35770ef17af0255e6e17ddeb848dde42c79db89e`
**CI run:** https://github.com/smael-aftersearch/ABAFramework/actions/runs/43286946630 — **PASS**

## Verified commands / behavior

| Gate | Outcome | Notes |
| --- | --- | --- |
| Existing ABA governance and AI QA | PASS | Original solution and Python negative tests preserved |
| Fresh `dotnet new install ./templates/aba-api` | PASS | Template registered in GitHub Actions |
| `dotnet new aba-api -n Pilot.QuickStart` | PASS | New domain-neutral solution generated |
| Restore + Release build of generated solution | PASS | 0 compiler warnings / 0 errors reported |
| `dotnet test` on generated project | PASS | 10 passed, 0 failed, 0 skipped |
| Generated ASP.NET host startup | PASS | Kestrel launched in local Development mode |
| `GET /health/live` | PASS | Body validated as Healthy |
| `GET /openapi/v1.json` | PASS | Generated OpenAPI includes system status endpoint |
| Interactive Scalar route | PASS | HTTP redirect followed; page content validated |
| Project-level Skill / AI docs | PASS | Template metadata and file existence checks |

## Scope and limitations

This verifies a **portable, empty-domain API starter**, not the Academy example as the default. The generated project includes no fake business bounded contexts. It has standards-based package references for JWT bearer authentication; non-development startup is guarded against an unconfigured OIDC Authority/Audience. Real identity-provider integration and authenticated request/tenant-isolation tests are not yet supplied.

PostgreSQL/EF Core, Wolverine transactional Outbox/Inbox, Redis distributed caching, migrations, real brokerage, load/chaos testing, deployment, and multitenancy **remain unimplemented** in the generic generated project. Academy has local development-only substitutes; do not confuse them with production adapters.

## Reproduce

From the repository root with .NET 10 SDK and NuGet access:

```bash
python3 scripts/test_starter_template.py
bash scripts/verify-starter.sh
```

See `templates/aba-api/README.md` for project creation instructions. GitHub CI status is the evidence, not this narrative alone.
