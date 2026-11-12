#!/usr/bin/env python3
"""No-SDK validation of the reusable, domain-neutral dotnet-new template."""
import json
from pathlib import Path
import unittest
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / 'templates' / 'aba-api'

class GenericTemplateTests(unittest.TestCase):
    def test_dotnet_new_metadata(self):
        meta = json.loads((TEMPLATE / '.template.config/template.json').read_text())
        self.assertEqual(meta['shortName'], 'aba-api')
        self.assertEqual(meta['sourceName'], 'Aba.Starter')
        self.assertEqual(meta['tags']['language'], 'C#')

    def test_starter_solution_and_projects_are_consistent(self):
        slnx = ET.parse(TEMPLATE / 'Aba.Starter.slnx').getroot()
        declared = {p.attrib['Path'] for p in slnx.findall('Project')}
        actual = {p.relative_to(TEMPLATE).as_posix() for p in TEMPLATE.rglob('*.csproj')}
        self.assertEqual(declared, actual)
        self.assertEqual(len(actual), 2)
        for project in TEMPLATE.rglob('*.csproj'):
            ET.parse(project)

    def test_documented_ai_and_test_policy(self):
        for path in ('AGENTS.md', 'docs/ai/INDEX.md', 'docs/testing/TEST-STRATEGY.md',
                     'metadata/contexts.json', 'README.md', 'global.json'):
            with self.subTest(path=path):
                self.assertTrue((TEMPLATE / path).is_file())
        self.assertEqual(json.loads((TEMPLATE / 'metadata/contexts.json').read_text())['contexts'], [])

    def test_no_sample_business_code_is_implicitly_copied(self):
        code = '\n'.join(p.read_text() for p in TEMPLATE.rglob('*.cs'))
        self.assertNotIn('namespace Academy', code)
        self.assertNotIn('Academy.', code)
        self.assertNotIn('PaymentCapturedV1', code)

    def test_api_baseline_has_real_generated_openapi_and_auth_guard(self):
        host = (TEMPLATE / 'src/Aba.Starter.Api/Program.cs').read_text()
        self.assertIn('AddOpenApi()', host)
        self.assertIn('MapOpenApi()', host)
        self.assertIn('MapScalarApiReference', host)
        self.assertIn('FallbackPolicy', host)
        self.assertIn('StartupSecurityGuard.Validate', host)
        self.assertIn('MapHealthChecks', host)
        self.assertIn('AddRateLimiter', host)

    def test_xunit_and_companion_script_exist(self):
        proj = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj').read_text()
        self.assertIn('PackageReference Include="xunit"', proj)
        self.assertIn('Microsoft.AspNetCore.Mvc.Testing', proj)
        script = (ROOT / 'scripts/verify-starter.sh').read_text()
        for gate in ('dotnet new aba-api', 'dotnet build', 'dotnet test', 'health/live', 'openapi/v1.json'):
            self.assertIn(gate, script)

if __name__ == '__main__':
    unittest.main(verbosity=2)
