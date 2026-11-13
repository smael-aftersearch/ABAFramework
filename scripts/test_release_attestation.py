#!/usr/bin/env python3
"""Positive, negative and invariant tests for the shared release verifier.

The gh executable is a local test double. Real signed GitHub attestation
verification is performed only by the main-branch provenance Actions job.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("verify_attested_artifact.sh")
GOOD_SHA = "a" * 40
WRONG_SHA = "0" * 40
FAKE_GH = '''#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ["GH_CALL_LOG"], "a", encoding="utf-8") as log:
    log.write(json.dumps(args) + "\\n")
if args[:2] != ["attestation", "verify"]:
    sys.exit(3)
payload = pathlib.Path(args[2]).read_bytes()
wrong = "0000000000000000000000000000000000000000" in args
mutated = payload.endswith(b"altered-after-scan")
mode = os.environ.get("FAKE_GH_MODE", "normal")
if (mutated and mode != "accept_tamper") or (wrong and mode != "accept_wrong_origin"):
    sys.exit(1)
print('{"verified":true}')
'''


class SignedArtifactVerifierTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.artifact = self.root / "template.nupkg"
        self.artifact.write_bytes(b"ci-test-fixture-not-a-signature")
        self.output = self.root / "evidence" / "verified.json"
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        fake_gh = bin_dir / "gh"
        fake_gh.write_text(FAKE_GH, encoding="utf-8")
        fake_gh.chmod(0o755)
        self.log = self.root / "gh-calls.jsonl"
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
                        GH_TOKEN="ci-fixture-not-a-secret", GITHUB_REPOSITORY="owner/repo",
                        GITHUB_SHA=GOOD_SHA, GH_CALL_LOG=str(self.log),
                        TMPDIR=str(self.root))

    def verify(self, **env_updates):
        env = dict(self.env)
        env.update(env_updates)
        # Resolve bash from the fixture's explicit PATH. Windows CreateProcess
        # otherwise prefers the WSL bash shim in System32 over Git Bash.
        bash = shutil.which("bash", path=env.get("PATH"))
        if not bash:
            self.fail("bash is required to run the signed-artifact verifier")
        return subprocess.run([bash, str(SCRIPT), str(self.artifact), str(self.output)],
                              env=env, text=True, capture_output=True, check=False)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_accepts_valid_signed_identity_and_checks_both_negative_cases(self):
        result = self.verify()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(self.output.is_file())
        self.assertEqual(3, len(self.calls()))
        positive, tampered, wrong_source = self.calls()
        self.assertIn("--source-ref", positive)
        self.assertIn("refs/heads/main", positive)
        self.assertIn(GOOD_SHA, positive)
        self.assertIn("--format", positive)
        self.assertIn(GOOD_SHA, tampered)
        self.assertIn(WRONG_SHA, wrong_source)
        self.assertNotIn(WRONG_SHA, positive)

    def test_rejects_verifier_that_accepts_mutated_artifacts(self):
        result = self.verify(FAKE_GH_MODE="accept_tamper")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("modified signed artifact", result.stderr)

    def test_rejects_verifier_that_accepts_wrong_source_sha(self):
        result = self.verify(FAKE_GH_MODE="accept_wrong_origin")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("wrong source commit", result.stderr)

    def test_missing_token_and_invalid_source_sha_fail_closed(self):
        for updates in ({"GH_TOKEN": ""}, {"GITHUB_SHA": "invalid"}):
            with self.subTest(updates=updates):
                if self.log.exists():
                    self.log.unlink()
                result = self.verify(**updates)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(self.log.exists(), "No verifier call on invalid inputs")


if __name__ == "__main__":
    unittest.main(verbosity=2)
