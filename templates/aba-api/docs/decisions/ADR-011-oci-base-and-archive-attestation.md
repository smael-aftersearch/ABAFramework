# ADR-011 — Immutable reviewed .NET OCI bases and attested scanned Docker archive

**Status:** Source/build integrity, not a published registry release.
**Owner:** Release Engineering; Product/Operations own actual deployment.
No domain contracts or production credentials are modified.

## Immutable SDK/runtime base selection

The generated multi-stage Dockerfile now pins **multi-architecture
manifest-list digests** for Microsoft's .NET 10 Ubuntu Noble SDK and
ASP.NET runtime, resolved via docker buildx imagetools inspect and
reviewed with current security inventory. The authoritative
oci-base-lock.json records the exact trusted MCR name, stage,
SHA256 digest, review date and source links. Floating major-version
tags alone are disallowed.

tools/oci_base_lock.py is a fail-closed validator for only the
trusted SDK/runtime stages, exactly the reviewed SHA256 digest and
non-root runtime USER $APP_UID. Its independent negative tests
deliberately inject altered digests, floating references,
untrusted registries, extra image stages, missing lock or
root execution and require rejection. Both upstream and generated
build/scan workflows invoke the validator before building.
Updating digests is a **reviewed code change**, never an
uncontrolled floating-tag build; refresh promptly after security
advisories, rebuild, rescan and rerun all gates.

## What the CI actually signs

On main pushes, after the real Trivy fixable-CRITICAL
runtime scan succeeds, CI exports the **exact scanned local Docker
image** via docker save and gzip. It records archive SHA256,
removes the tagged local image, loads the archive and checks the
restored Docker image ID equals the pre-export scanned image ID.
The scan job **does not sign** this archive: a failed database, Redis
restart or Keycloak test must not yield signed release-candidate
provenance. The final job depends on eight successful upstream jobs,
downloads the already-scanned archive, rechecks its SHA256 and image ID,
then signs SLSA Build Provenance of those exact bytes via OIDC.
gh attestation verify requires the repository, main source ref
and current Git commit. A same-name mutated archive MUST fail
verification. The archive, SHA256 and verification results are
uploaded as CI artifacts.

**This is not a signature of an OCI registry manifest digest.**
The source framework does not push to ghcr.io, Docker Hub or
customer registries, and it does not silently grant deploy
permissions. A consumer must independently verify the signed
archive hash before docker load, then validate the loaded image
ID and their target deploy digest/architecture. A later
published OCI image needs registry-level digest signing
(Cosign/Notation or equivalent), protected release policies,
immutable labels, a controlled promotion pipeline and separate
production change approval.

## Security boundaries and rollback

The generated downstream API inherits immutable base checks,
Docker scanning and the NuGet advisory/SPDX gate; it **does not**
inherit the upstream archive's cryptographic identity. Each
product must sign its own published image and release.
Trivy currently fails only for fixable CRITICAL runtime issues;
HIGH and unfixed policies, vendor CVE exceptions, package
signatures, deployment gates, Kubernetes policies and SLSA
attestation trust rules remain Release/Operations approvals.
Keep previous reviewed digests for rollback only if their
vulnerability posture remains acceptable; rollback is not
permission to deploy known exploitable base images.
