#!/usr/bin/env python3
"""Fail-closed vulnerability gate and SPDX 2.3 inventory of restored NuGet packages.

This is not an OCI scanner, release signer or provenance attestation.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
from urllib.parse import quote
from xml.etree import ElementTree


class SupplyChainError(ValueError):
    pass


def load(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise SupplyChainError(f"Cannot read valid JSON: {path}") from exc


def project_paths(root, slnx):
    root, slnx = root.resolve(), slnx.resolve()
    if not slnx.is_file() or not slnx.is_relative_to(root):
        raise SupplyChainError("Solution must exist inside project root")
    try:
        elements = ElementTree.parse(slnx).findall(".//Project")
    except ElementTree.ParseError as exc:
        raise SupplyChainError("Invalid .slnx") from exc
    paths = []
    for e in elements:
        raw = e.attrib.get("Path", "")
        if not raw or "\\" in raw:
            raise SupplyChainError("Invalid project path")
        p = (slnx.parent / raw).resolve()
        if not p.is_relative_to(root) or p.suffix != ".csproj" or not p.is_file():
            raise SupplyChainError("Missing/unsafe .slnx project")
        paths.append(p)
    if not paths or len(paths) != len(set(paths)):
        raise SupplyChainError("No unique projects in solution")
    return paths


def packages_from_assets(projects):
    result = {}
    for project in projects:
        data = load(project.parent / "obj" / "project.assets.json")
        libs, targets = data.get("libraries"), data.get("targets")
        if not isinstance(libs, dict) or not isinstance(targets, dict) or not targets:
            raise SupplyChainError("Missing NuGet restore targets")
        target_packages = {
            k.lower()
            for target in targets.values() if isinstance(target, dict)
            for k, value in target.items()
            if isinstance(value, dict) and value.get("type", "").lower() == "package"
        }
        for name, info in libs.items():
            if not isinstance(info, dict) or info.get("type", "").lower() != "package":
                continue
            if name.lower() not in target_packages:
                continue
            parts = name.rsplit("/", 1)
            if len(parts) != 2 or not all(parts):
                raise SupplyChainError("Invalid package/version in restore graph")
            result[(parts[0].lower(), parts[1])] = parts
    if not result:
        raise SupplyChainError("Empty restored package inventory")
    return [result[x] for x in sorted(result)]


def check_audit(audit, projects):
    if not isinstance(audit, dict) or audit.get("version") != 1:
        raise SupplyChainError("Invalid NuGet audit JSON version")
    parameters = audit.get("parameters", "")
    if not isinstance(parameters, str) or "--vulnerable" not in parameters or "--include-transitive" not in parameters:
        raise SupplyChainError("Audit command omitted --vulnerable or --include-transitive")
    sources = audit.get("sources")
    if not isinstance(sources, list) or not sources or not all(
        isinstance(x, str) and x.startswith("https://") for x in sources
    ):
        raise SupplyChainError("No verified HTTPS advisory source")
    if audit.get("problems", []):
        raise SupplyChainError("NuGet advisory query or restore returned problems")
    records = audit.get("projects")
    if not isinstance(records, list) or not records:
        raise SupplyChainError("Missing NuGet audit projects")
    actual = set()
    findings = []
    for item in records:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise SupplyChainError("Malformed project audit")
        name = Path(item["path"].replace("\\", "/")).name.lower()
        if name in actual:
            raise SupplyChainError("Duplicate project audit")
        actual.add(name)
        frameworks = item.get("frameworks")
        # In dotnet package list --vulnerable JSON, a project with no
        # advisories legitimately has frameworks: []. We still demand
        # complete project coverage, HTTPS sources and an intact restore graph.
        # NuGet omits 'frameworks' entirely when no vulnerable packages
        # were found. Project coverage, trusted advisory sources and the
        # restored assets graph remain mandatory independent checks.
        if frameworks is None:
            frameworks = []
        if not isinstance(frameworks, list):
            raise SupplyChainError("Invalid audited framework list")
        for framework in frameworks:
            if not isinstance(framework, dict) or not framework.get("framework"):
                raise SupplyChainError("Malformed framework audit")
            for kind in ("topLevelPackages", "transitivePackages"):
                rows = framework.get(kind, [])
                if not isinstance(rows, list):
                    raise SupplyChainError("Malformed package audit list")
                for pkg in rows:
                    if not isinstance(pkg, dict) or not pkg.get("id"):
                        raise SupplyChainError("Malformed audited package")
                    advisory = pkg.get("vulnerabilities", [])
                    if not isinstance(advisory, list):
                        raise SupplyChainError("Malformed vulnerability list")
                    for a in advisory:
                        if not isinstance(a, dict):
                            raise SupplyChainError("Malformed advisory")
                        findings.append(f"{pkg['id']}@{pkg.get('resolvedVersion', '?')} ({a.get('severity', '?')})")
    if actual != {p.name.lower() for p in projects}:
        raise SupplyChainError("NuGet audit did not cover all solution projects")
    if findings:
        raise SupplyChainError("Known vulnerable NuGet packages: " + ", ".join(findings[:10]))


def spdx(package_list):
    packages, relationships = [], []
    digest = hashlib.sha256(json.dumps(package_list).encode("utf-8")).hexdigest()
    for name, version in package_list:
        ident = "SPDXRef-NuGet-" + hashlib.sha256(f"{name.lower()}@{version}".encode()).hexdigest()[:24]
        packages.append({
            "SPDXID": ident, "name": name, "versionInfo": version, "filesAnalyzed": False,
            "downloadLocation": "NOASSERTION", "licenseConcluded": "NOASSERTION",
            "licenseDeclared": "NOASSERTION", "copyrightText": "NOASSERTION",
            "externalRefs": [{
                "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl",
                "referenceLocator": f"pkg:nuget/{quote(name, safe='')}@{quote(version, safe='')}",
            }],
        })
        relationships.append({
            "spdxElementId": "SPDXRef-DOCUMENT",
            "relatedSpdxElement": ident, "relationshipType": "DESCRIBES",
        })
    return {
        "spdxVersion": "SPDX-2.3", "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT", "name": "ABA-resolved-NuGet-packages",
        "documentNamespace": f"https://spdx.org/spdxdocs/aba-nuget-{digest}",
        "creationInfo": {
            "creators": ["Tool: aba-supply-chain/1"],
            "created": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        },
        "packages": packages, "relationships": relationships,
        "documentDescribes": [x["SPDXID"] for x in packages],
    }


def verify(root, solution, audit, output):
    projects = project_paths(root, solution)
    check_audit(load(audit), projects)
    packages = packages_from_assets(projects)
    if not output.resolve().is_relative_to(root.resolve()):
        raise SupplyChainError("Output must be inside project root")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(spdx(packages), indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {len(projects)} projects; {len(packages)} resolved NuGet packages; no known advisories")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--solution", type=Path, required=True)
    parser.add_argument("--audit-json", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        verify(args.root, args.solution, args.audit_json, args.output)
        return 0
    except (SupplyChainError, OSError) as exc:
        print(f"FAIL: supply-chain gate: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
