#!/usr/bin/env python3
"""Offline fail-closed positive and negative tests for NuGet advisory and SPDX gate."""
import json
from pathlib import Path
import tempfile
import unittest
from supply_chain import SupplyChainError, verify


class SupplyChainTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.csproj = self.root / "src" / "Demo" / "Demo.csproj"
        self.csproj.parent.mkdir(parents=True)
        self.csproj.write_text("<Project/>")
        self.slnx = self.root / "Demo.slnx"
        self.slnx.write_text('<Solution><Project Path="src/Demo/Demo.csproj" /></Solution>')
        self.assets = self.csproj.parent / "obj" / "project.assets.json"
        self.assets.parent.mkdir()
        self.assets.write_text(json.dumps({
            "libraries": {"Safe/1.2.3": {"type": "package"}},
            "targets": {"net10.0": {"Safe/1.2.3": {"type": "package"}}},
        }))
        self.payload = {
            "version": 1, "parameters": "--vulnerable --include-transitive",
            "sources": ["https://api.nuget.org/v3/index.json"],
            "projects": [{"path": str(self.csproj), "frameworks": [
                {"framework": "net10.0", "topLevelPackages": []}]}],
        }
        self.audit = self.root / "audit.json"
        self.output = self.root / "out" / "sbom.json"

    def check(self):
        self.audit.write_text(json.dumps(self.payload))
        verify(self.root, self.slnx, self.audit, self.output)

    def test_safe_packages_export_spdx(self):
        self.check()
        doc = json.loads(self.output.read_text())
        self.assertEqual(doc["spdxVersion"], "SPDX-2.3")
        self.assertEqual(doc["packages"][0]["name"], "Safe")
        self.assertEqual(doc["packages"][0]["versionInfo"], "1.2.3")

    def test_direct_vulnerability_is_rejected(self):
        self.payload["projects"][0]["frameworks"][0]["topLevelPackages"] = [{
            "id": "Bad", "vulnerabilities": [{"severity": "High"}],
        }]
        with self.assertRaisesRegex(SupplyChainError, "Known vulnerable"):
            self.check()
        self.assertFalse(self.output.exists())

    def test_transitive_vulnerability_is_rejected(self):
        self.payload["projects"][0]["frameworks"][0]["transitivePackages"] = [{
            "id": "Bad", "vulnerabilities": [{"severity": "Moderate"}],
        }]
        with self.assertRaisesRegex(SupplyChainError, "Known vulnerable"):
            self.check()

    def test_missing_advisory_sources_is_rejected(self):
        self.payload["sources"] = []
        with self.assertRaisesRegex(SupplyChainError, "advisory source"):
            self.check()

    def test_missing_project_audit_is_rejected(self):
        self.payload["projects"] = []
        with self.assertRaisesRegex(SupplyChainError, "Missing NuGet audit"):
            self.check()

    def test_restore_graph_missing_is_rejected(self):
        self.assets.unlink()
        with self.assertRaisesRegex(SupplyChainError, "valid JSON"):
            self.check()

    def test_advisory_error_is_rejected(self):
        self.payload["problems"] = ["feed unreachable"]
        with self.assertRaisesRegex(SupplyChainError, "problems"):
            self.check()

    def test_scan_without_vulnerability_flag_is_rejected(self):
        self.payload["parameters"] = "--format json"
        with self.assertRaisesRegex(SupplyChainError, "omitted"):
            self.check()

    def test_unsafe_solution_is_rejected(self):
        self.slnx.write_text('<Solution><Project Path="../../escape.csproj" /></Solution>')
        with self.assertRaisesRegex(SupplyChainError, "unsafe"):
            self.check()

    def test_empty_package_inventory_is_rejected(self):
        self.assets.write_text(json.dumps({"libraries": {}, "targets": {"net10.0": {}}}))
        with self.assertRaisesRegex(SupplyChainError, "Empty restored"):
            self.check()


if __name__ == "__main__":
    unittest.main(verbosity=2)
