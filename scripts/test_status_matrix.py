#!/usr/bin/env python3
"""Fail-closed checks for machine-readable ABA feature status and evidence."""
import copy
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("status_matrix", ROOT / "scripts/status_matrix.py")
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
CATALOG = json.loads((ROOT / "docs/implementation-status.json").read_text(encoding="utf-8"))


class StatusMatrixTests(unittest.TestCase):
    def test_real_capabilities_and_complete_readme(self):
        entries = module.validate(CATALOG)
        self.assertGreaterEqual(len(entries), 35)
        self.assertIn("Implemented", module.render_section(CATALOG))
        self.assertIn("Not implemented", module.render_section(CATALOG))
        self.assertIn("P0", module.render_details(CATALOG))
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertEqual(readme, module.replace_readme(readme, module.render_section(CATALOG)))

    def test_missing_executable_evidence_is_rejected(self):
        altered = copy.deepcopy(CATALOG)
        item = next(x for x in altered["features"] if x["status"] == "implemented")
        item["tests"] = []
        with self.assertRaises(ValueError):
            module.validate(altered)

    def test_fabricated_implemented_path_is_rejected(self):
        altered = copy.deepcopy(CATALOG)
        item = next(x for x in altered["features"] if x["status"] == "implemented")
        item["evidence"] = ["nonexistent/fictional-implementation.cs"]
        with self.assertRaises(ValueError):
            module.validate(altered)

    def test_not_implemented_cannot_claim_delivered_implementation(self):
        altered = copy.deepcopy(CATALOG)
        item = next(x for x in altered["features"] if x["status"] == "not_started")
        item["delivered"] = "Complete"
        with self.assertRaises(ValueError):
            module.validate(altered)

    def test_no_silent_duplicate_or_unowned_tasks(self):
        altered = copy.deepcopy(CATALOG)
        altered["features"].append(copy.deepcopy(altered["features"][0]))
        with self.assertRaises(ValueError):
            module.validate(altered)
        altered = copy.deepcopy(CATALOG)
        altered["features"][0]["owner"] = ""
        with self.assertRaises(ValueError):
            module.validate(altered)

    def test_readme_markers_are_mandatory(self):
        with self.assertRaises(ValueError):
            module.replace_readme("no markers here", module.render_section(CATALOG))


if __name__ == "__main__":
    unittest.main(verbosity=2)
