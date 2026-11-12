# ABA release integrity — NuGet, OCI and provenance gates

This document describes technical controls for the **domain-neutral**
`dotnet new aba-api` template. It is not approval to publish to NuGet.org,
deploy a customer product or manage production secrets.

**Actual verified execution:** [GitHub Actions 43317295032](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43317295032)
passed all 8 jobs, including the commit-pinned Trivy runtime scan and
keyless signed/verified NuGet template provenance. The package's
Sigstore public-good Rekor record is available as
[GitHub attestation 66918425](https://github.com/smael-aftersearch/ABAFramework/attestations/66918425).
A same-name modified package was intentionally rejected.
This signs an internal CI-produced template package, **not** a published
NuGet.org binary or a container image.

## Implemented CI definitions

**Dependency audit:** both the default solution and the independent
infrastructure test project are restored, scanned for direct/transitive
NuGet advisories and exported as two SPDX 2.3 inventories. Incomplete or
vulnerable evidence fails closed.

**OCI runtime scan:** CI builds the final .NET API runtime Docker stage
and runs the commit-pinned Trivy action. A **fixable CRITICAL**
vulnerability fails the job. The scanner's JSON report and a local
built-image ID are retained. Generated projects inherit the same scan.
HIGH, unfixed and non-runtime risks are not presently hard failures;
the CI image ID is not an immutable **base image** pin or published OCI
digest.

**Signed NuGet template provenance:** on pushes to `main`, after all
governance, integration, OIDC, packaging, runtime and image-security jobs
succeed, CI downloads the tested versioned `.nupkg` artifact, records
its SHA256, uses GitHub Actions' OIDC identity to produce a keyless
Sigstore/SLSA build provenance attestation, and calls
`gh attestation verify` against that source commit. A deliberately
modified same-name package is required to fail verification.
The attestation is held in GitHub's attestation service, not a
customer-facing production registry.

An operator who downloaded the template artifact should run:

```bash
gh attestation verify ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg \
  --repo smael-aftersearch/ABAFramework --source-ref refs/heads/main
```

Validate source commit, signer identity and digest, not just the filename.
The new OCI/provenance steps must pass CI before being counted as
executed evidence. Do not bypass failures with `continue-on-error`.

## Remaining P0 gates

Pin SDK/runtime container base images to reviewed immutable digests,
approve a HIGH/CRITICAL and unfixed-advisory policy, prove a signed OCI
publication, introduce protected immutable release tags and signed
published artifacts, approve a secret manager and key rotation, and test
deployment with TLS and rollback in an actual owner-approved target.
Source/CI provenance does not alone secure a product's runtime.
