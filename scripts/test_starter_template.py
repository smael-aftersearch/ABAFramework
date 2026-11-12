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
        self.assertEqual(len(declared), 3)
        self.assertEqual(len(actual), 4)
        self.assertEqual(actual - declared, {'tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj'})
        for project in TEMPLATE.rglob('*.csproj'):
            ET.parse(project)

    def test_documented_ai_and_test_policy(self):
        for path in ('AGENTS.md', 'docs/ai/INDEX.md', 'docs/testing/TEST-STRATEGY.md',
                     'metadata/contexts.json', 'README.md', 'global.json',
                     '.agents/skills/aba-engineering/SKILL.md',
                     '.agents/skills/aba-engineering/agents/openai.yaml'):
            with self.subTest(path=path):
                self.assertTrue((TEMPLATE / path).is_file())
        self.assertEqual(json.loads((TEMPLATE / 'metadata/contexts.json').read_text())['contexts'], [])

    def test_platform_providers_messaging_and_redis_are_real_packages(self):
        package = (TEMPLATE / 'src/Aba.Starter.Platform/Aba.Starter.Platform.csproj').read_text()
        for name in ('Microsoft.EntityFrameworkCore.SqlServer',
                     'Npgsql.EntityFrameworkCore.PostgreSQL',
                     'Microsoft.Extensions.Caching.StackExchangeRedis',
                     'WolverineFx.EntityFrameworkCore', 'WolverineFx.Postgresql',
                     'WolverineFx.SqlServer'):
            self.assertIn(name, package)
        program = (TEMPLATE / 'src/Aba.Starter.Api/Program.cs').read_text()
        self.assertIn('builder.AddAbaPlatform()', program)

    def test_infrastructure_tests_fail_closed_and_are_separate(self):
        default_project = TEMPLATE / 'tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj'
        external_project = TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/Aba.Starter.Platform.IntegrationTests.csproj'
        self.assertTrue(external_project.is_file())
        self.assertNotIn('AbaExternalInfrastructureTests', default_project.read_text())
        self.assertFalse((TEMPLATE / 'tests/Aba.Starter.Api.Tests/AbaExternalInfrastructureTests.cs').exists())
        tests = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/AbaExternalInfrastructureTests.cs').read_text()
        self.assertNotIn('IsNullOrWhiteSpace(connection)) return', tests)
        self.assertIn('Assert.False(string.IsNullOrWhiteSpace(connection)', tests)
        script = (ROOT / 'scripts/verify-platform-services.sh').read_text()
        self.assertIn('dotnet test "$INTEGRATION"', script)

    def test_ai_skill_has_trigger_metadata(self):
        skill = (TEMPLATE / '.agents/skills/aba-engineering/SKILL.md').read_text()
        self.assertTrue(skill.startswith('---\\n') or skill.startswith('---\n'))
        self.assertIn('name: aba-engineering', skill)
        self.assertIn('description:', skill)
        self.assertIn('dotnet test', skill)

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

    def test_generated_container_is_nonroot_and_loopback(self):
        dockerfile = (TEMPLATE / 'Dockerfile').read_text()
        compose = (TEMPLATE / 'compose.yaml').read_text()
        self.assertIn('USER $APP_UID', dockerfile)
        self.assertIn('mcr.microsoft.com/dotnet/aspnet:10.0', dockerfile)
        self.assertIn('127.0.0.1:5174:8080', compose)
        self.assertIn('no-new-privileges:true', compose)
        self.assertIn('Non-development API requires', 
                      (TEMPLATE / 'src/Aba.Starter.Api/Platform/StartupSecurityGuard.cs').read_text())

    def test_package_can_be_built_and_installed(self):
        package = (ROOT / 'packaging/ABAFramework.GenericApi.Template.csproj').read_text()
        self.assertIn('<PackageType>Template</PackageType>', package)
        self.assertIn('<ContentTargetFolders>content</ContentTargetFolders>', package)
        script = (ROOT / 'scripts/pack-template.sh').read_text()
        self.assertIn('dotnet pack', script)
        self.assertIn('dotnet new install "$PACKAGE"', script)

    def test_new_context_generator_present_and_no_fictional_default_contexts(self):
        generator = (TEMPLATE / 'tools/new_context.py').read_text()
        self.assertIn('def create(name, with_db=False):', generator)
        self.assertIn('--with-db', generator)
        self.assertIn('def check():', generator)
        self.assertIn('Domain', generator)
        self.assertIn('forbidden', generator)
        self.assertEqual(json.loads((TEMPLATE / 'metadata/contexts.json').read_text())['contexts'], [])
        self.assertTrue((TEMPLATE / 'docs/context-generator.md').is_file())

    def test_generated_project_has_automatic_and_optional_integration_ci(self):
        ci = (TEMPLATE / '.github/workflows/ci.yml').read_text()
        integration = (TEMPLATE / '.github/workflows/integration.yml').read_text()
        self.assertIn("push:", ci)
        self.assertIn("pull_request:", ci)
        self.assertIn("contents: read", ci)
        self.assertIn("tools/aba_task.py verify --level build", ci)
        self.assertIn("workflow_dispatch:", integration)
        self.assertIn("tools/aba_task.py verify --level full", integration)
        for provider in ("postgres", "sqlserver", "redis"):
            self.assertIn(provider + ":", integration)
        self.assertNotIn("publish", ci.casefold())

    def test_portable_agent_task_protocol_is_bundled_and_negative_tested(self):
        planner = TEMPLATE / 'tools/aba_task.py'
        tests = TEMPLATE / 'tools/test_aba_task.py'
        docs = TEMPLATE / 'docs/ai/TASK-PROTOCOL.md'
        self.assertTrue(planner.is_file())
        self.assertTrue(tests.is_file())
        self.assertTrue(docs.is_file())
        self.assertIn('def verify(level:', planner.read_text())
        self.assertIn('NEEDS_OWNER', planner.read_text())
        verify_script = (ROOT / 'scripts/verify-starter.sh').read_text()
        self.assertIn('tools/test_aba_task.py', verify_script)
        self.assertIn('tools/aba_task.py" verify --level static', verify_script)

    def test_xunit_and_companion_script_exist(self):
        proj = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj').read_text()
        self.assertIn('PackageReference Include="xunit"', proj)
        self.assertIn('Microsoft.AspNetCore.Mvc.Testing', proj)
        script = (ROOT / 'scripts/verify-starter.sh').read_text()
        for gate in ('dotnet new aba-api', 'dotnet build', 'dotnet test', 'health/live', 'openapi/v1.json'):
            self.assertIn(gate, script)

if __name__ == '__main__':
    unittest.main(verbosity=2)
