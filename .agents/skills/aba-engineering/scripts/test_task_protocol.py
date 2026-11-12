#!/usr/bin/env python3
"""Positive and negative regression tests for the deterministic ABA task protocol."""
from pathlib import Path
import sys
import tempfile
import os
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
import task_protocol as protocol

ROOT = Path(os.environ.get('ABA_REPO_ROOT', Path.cwd())).resolve()


class TestTaskProtocol(unittest.TestCase):
    def test_refund_payment_reading_and_test_plan(self):
        planned = protocol.classify_task(ROOT, 'Add refund using an integration event and idempotent Outbox',
                                         ['samples/Academy/Payments.Contracts/PaymentContracts.cs'])
        self.assertIn('Payments', planned['possible_contexts'])
        self.assertFalse(planned['ownership_confirmed'])
        self.assertIn('contract-catalog.yaml', planned['reading_paths'])
        cats = [x['category'] for x in planned['test_categories']]
        self.assertIn('domain', cats)
        self.assertIn('contracts', cats)
        self.assertIn('messaging', cats)
        self.assertTrue(planned['existing_test_inventory']['console_smoke_source'])
        self.assertEqual(planned['existing_test_inventory']['xunit_test_projects'], [])

    def test_api_security_plan(self):
        planned = protocol.classify_task(ROOT, 'Secure the OAuth API endpoint with tenant permissions',
                                         ['samples/Academy/Host/Program.cs'])
        categories = {x['category'] for x in planned['test_categories']}
        self.assertTrue({'api', 'security'}.issubset(categories))
        self.assertIn('docs/security.md', planned['reading_paths'])

    def test_unknown_business_owner_remains_unresolved(self):
        planned = protocol.classify_task(ROOT, 'Add a lunar calendar export with no existing owner', [])
        self.assertFalse(planned['possible_contexts'])
        self.assertFalse(planned['ownership_confirmed'])

    def test_path_traversal_is_rejected(self):
        with self.assertRaises(ValueError):
            protocol.classify_task(ROOT, 'Architecture change', ['../../etc/passwd'])
        with self.assertRaises(ValueError):
            protocol.write_output(ROOT, '../outside.json', '{}')

    def test_no_runtime_marks_not_run_even_if_static_pass(self):
        success = lambda root, name, command, timeout: dict(name=name, command=command,
                          status='PASS', exit_code=0, duration_seconds=0, failure_output_redacted='')
        with patch.object(protocol.shutil, 'which', return_value=None), patch.object(protocol, 'check_command', side_effect=success):
            report = protocol.verify_repo(ROOT, 'all')
        self.assertEqual(report['summary']['PASS'], 6)
        self.assertEqual(report['summary']['NOT_RUN'], 3)
        self.assertEqual(report['summary']['FAIL'], 0)
        self.assertIn('NOT_RUN', protocol.format_verification(report))

    def test_failed_static_gate_cannot_be_laundered_as_success(self):
        def fail_once(root, name, command, timeout):
            status = 'FAIL' if name == 'ABA governance' else 'PASS'
            return {'name': name, 'command': command, 'status': status,
                    'exit_code': 4 if status == 'FAIL' else 0, 'duration_seconds': 0}
        with patch.object(protocol, 'check_command', side_effect=fail_once):
            report = protocol.verify_repo(ROOT, 'static')
        self.assertEqual(report['summary']['FAIL'], 1)
        self.assertEqual(report['summary']['PASS'], 5)

    def test_real_command_failure_is_reported(self):
        result = protocol.check_command(ROOT, 'deliberate failing command',
                    [sys.executable, '-c', 'import sys;sys.exit(9)'], 10)
        self.assertEqual(result['status'], 'FAIL')
        self.assertEqual(result['exit_code'], 9)

    def test_evidence_file_must_be_in_safe_directory(self):
        with self.assertRaises(ValueError):
            protocol.write_output(ROOT, 'README.md', 'unsafe overwrite')
        with tempfile.TemporaryDirectory(dir=ROOT / 'artifacts' if (ROOT / 'artifacts').exists() else ROOT) as folder:
            # A temp directory alone must not bypass the fixed artifacts/verification boundary.
            with self.assertRaises(ValueError):
                protocol.write_output(ROOT, str(Path(folder).relative_to(ROOT) / 'test.json'), '{}')

    def test_redact_sensitive_failure_output(self):
        scrubbed = protocol.redact('Bearer abc.def password=hunter2 api_key:verysecret')
        self.assertNotIn('hunter2', scrubbed)
        self.assertNotIn('verysecret', scrubbed)
        self.assertNotIn('abc.def', scrubbed)


if __name__ == '__main__':
    unittest.main(verbosity=2)
