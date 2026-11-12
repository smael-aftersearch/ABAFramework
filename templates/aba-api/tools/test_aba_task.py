#!/usr/bin/env python3
"""Regression tests for the generated project's portable ABA task workflow."""
import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("aba_task", TOOLS / "aba_task.py")
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TaskPlannerTests(unittest.TestCase):
    def test_unknown_ownership_never_invented(self):
        p = module.plan("Create a new user feature", [])
        self.assertEqual(p["status"], "NEEDS_OWNER")

    def test_protects_unsafe_paths(self):
        with self.assertRaises(ValueError):
            module.plan("Unsafe", ["../secrets"])
        with self.assertRaises(ValueError):
            module.plan("Unsafe", ["/tmp/private"])
        with self.assertRaises(ValueError):
            module.plan("Unknown module", ["src/Modules/NotRegistered/Domain/Foo.cs"])

    def test_determines_technical_composition_ownership(self):
        p = module.plan(
            "Fix JSON errors", ["src/Aba.Starter.Api/Program.cs"])
        self.assertEqual(p["status"], "READY_FOR_REVIEW")
        self.assertIn("Host (composition only)", p["owners"])
        self.assertIn("negative", p["acceptance"])

    def test_context_reads_owner_documents_and_contracts(self):
        catalog = module.catalog()
        if not catalog:
            self.skipTest("This newly generated starter has no invented contexts")
        name = catalog[0]["name"]
        p = module.plan(
            "Version an event", [f"src/Modules/{name}/Contracts/NewEventV1.cs"])
        self.assertIn(name, p["owners"])
        self.assertIn(f"src/Modules/{name}/_Docs/ownership.yaml",
                      p["read_before_edit"])
        self.assertIn("docs/decisions/", p["read_before_edit"])

    def test_static_verification_runs_real_governance(self):
        outcome = module.verify("static")
        self.assertEqual("PASS", outcome["status"])
        self.assertTrue(outcome["steps"])
        self.assertIn("new_context.py", " ".join(outcome["steps"][0]["command"]))


if __name__ == "__main__":
    unittest.main(verbosity=2)
