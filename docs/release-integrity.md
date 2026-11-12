# ABA release integrity — NuGet advisories, immutable OCI bases and signed artifacts

This release policy concerns the generic `dotnet new aba-api` starter. It
does **not** authorize public NuGet.org/OCI publishing, customer deployments,
production keys or automatic registry promotion.

## Evidence and separation of products

Earlier complete [8/8 CI run 43325003437](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43325003437)
established a tested .NET 10 template, separately audited NuGet graphs,
Trivy runtime scan, true Keycloak, and signed/verified upstream NuGet
template provenance. A newer OCI archive-signing job independently
succeeded in [source CI 43327603146](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43327603146);
that *overall run* was not green because a separate experimental Redis
fixture needed a test fix. Confirm the latest fully successful run
before declaring this whole release gate accepted.

## Locked, reviewed Microsoft .NET bases

The default starter Dockerfile now pins both build and runtime stages to
**immutable multi-platform manifest-index digests** for .NET 10 Ubuntu
Noble. Exact sources and review date are stored in
`templates/aba-api/oci-base-lock.json`. The SDK uses
`mcr.microsoft.com/dotnet/sdk:10.0-noble`, and runtime uses
`mcr.microsoft.com/dotnet/aspnet:10.0-noble`; tags are informational and
**the SHA256 digest fixes the actual content**.

The Python validator `tools/oci_base_lock.py` and deliberate negative
tests `tools/test_oci_base_lock.py` fail builds for floating references,
unapproved registries, missing/changed digests, unreviewed stages, absent
locks or root execution. Both upstream and freshly generated CI enforce
these before building. Digest refresh requires a reviewed code change,
`docker buildx imagetools inspect`, fresh CI and image security scanning.
A fixed old digest is *reproducible*, not immune to new CVEs; review
regularly and immediately after upstream advisories.

## Source vulnerability checks

Both normal .NET solution and external-service integration project are
restored, scanned for **all known direct/transitive NuGet advisories**
and exported as two SPDX 2.3 inventories. Incomplete HTTPS advisory
sources, missing restore graphs and vulnerabilities fail closed.

Root and generated CI build the actual runtime Docker image and scan it
with commit-pinned Trivy. A **fixable CRITICAL** OS/library issue fails.
The scanner's JSON and built-image ID are retained. HIGH and unfixed
advisories are inventoried but are not yet automatic deployment blockers;
a Security-approved policy and exception process are missing.

## Signed artifact provenance, not signed OCI registry publication

After a successful **runtime image scan**, root CI on `main` exports
that exact tagged image with `docker save | gzip`, hashes the bytes,
removes the local tag, loads the archive and requires that the restored
Docker image ID equal the previously scanned ID. The archive is kept
as a CI artifact but is **not yet signed**. Only when **all eight**
independent governance, real-service, Redis restart, Keycloak, image,
packaging, vulnerability and container jobs have passed does the final
`template-provenance` job download that exact artifact, check its
SHA256 and Docker image ID again, and issue keyless SLSA provenance
through `actions/attest` for the **Docker-save archive**, verifying
repository, `refs/heads/main`, source commit and archive digest. A modified same-name archive
MUST fail verification. The artifact, hash, attestation JSON and Trivy
report are retained. This is stronger than signing a textual Dockerfile,
but it does **not** sign a published OCI registry manifest digest.

Separately, after all upstream CI jobs pass, the template NuGet
package artifact is signed with GitHub Actions OIDC/Sigstore provenance,
verified against the same commit, and a corrupted same-name package
is rejected. The original proven upstream NuGet example is
[GitHub attestation 66918425](https://github.com/smael-aftersearch/ABAFramework/attestations/66918425).

Given downloaded CI artifacts, operators can verify independently:

```bash
sha256sum --check image-archive-sha256.txt
gh attestation verify aba-runtime-image.tar.gz \
  --repo smael-aftersearch/ABAFramework --source-ref refs/heads/main \
  --source-digest <exact-reviewed-Git-commit>
# Compare the local docker load image ID to the evidence's built-image-id.txt.
```

Likewise verify the NuGet artifact's source ref and SHA256. Do not
substitute filename matching for signed digest equality. The upstream
framework archive attestation does not sign or authorize any
`dotnet new`-generated downstream application's image.

## Still-open Production gates

Sign the exact **published OCI registry digest**, protect tags and
release branch environments, authorize NuGet.org publishing and
separately attest the released binary, establish HIGH/unfixed advisory
policies and explicit CVE exceptions, approve secrets management and
key rotation, and verify deployment TLS, provenance promotion, rollout,
rollback and monitoring on an owner-approved target. See
[ADR-011](../templates/aba-api/docs/decisions/ADR-011-oci-base-and-archive-attestation.md).
