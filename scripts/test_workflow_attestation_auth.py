#!/usr/bin/env python3
"""Fail closed when Actions attestation verifiers receive literal credentials."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / ".github/workflows/verify.yml"
VERIFIER = ROOT / "scripts/verify_attested_artifact.sh"
EXPECTED = "$" + "{{ github.token }}"
TOKEN_LINE = re.compile(r"(?m)^[ \t]+GH_TOKEN:[ \t]*(.*?)[ \t]*$")


def valid_gh_token_bindings(source: str) -> bool:
    """Both signed-package and signed-image verifiers need an expanded token."""
    bindings = TOKEN_LINE.findall(source)
    return len(bindings) == 2 and all(value == EXPECTED for value in bindings)


class AttestationTokenBindingTests(unittest.TestCase):
    def test_checked_in_workflow_binds_both_verifiers_correctly(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        verifier = VERIFIER.read_text(encoding="utf-8")
        self.assertTrue(valid_gh_token_bindings(text), "GH_TOKEN must be unescaped for both verifiers")
        self.assertEqual(2, text.count("bash scripts/verify_attested_artifact.sh"))
        self.assertIn('gh attestation verify "$artifact"', verifier)
        self.assertIn('--source-ref refs/heads/main --source-digest "$GITHUB_SHA"', verifier)
        self.assertIn("wrong_sha='0000000000000000000000000000000000000000'", verifier)
        self.assertIn('gh attestation verify "$mutated"', verifier)
        self.assertIn('uses: actions/checkout@v4', text.split('  template-provenance:', 1)[1])

    def test_escaped_token_is_rejected(self):
        valid = "  GH_TOKEN: " + EXPECTED + "\n" + "  GH_TOKEN: " + EXPECTED
        self.assertTrue(valid_gh_token_bindings(valid))
        self.assertFalse(valid_gh_token_bindings(valid.replace(EXPECTED, "\\" + EXPECTED, 1)))

    def test_missing_or_literal_token_is_rejected(self):
        self.assertFalse(valid_gh_token_bindings("  GH_TOKEN: " + EXPECTED))
        self.assertFalse(valid_gh_token_bindings("  GH_TOKEN: literal\n  GH_TOKEN: " + EXPECTED))
        self.assertFalse(valid_gh_token_bindings("  GH_TOKEN: \n  GH_TOKEN: " + EXPECTED))


if __name__ == "__main__":
    unittest.main(verbosity=2)
