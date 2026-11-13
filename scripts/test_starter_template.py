#!/usr/bin/env python3
"""No-SDK validation of the reusable, domain-neutral dotnet-new template."""
import json
from pathlib import Path
import unittest
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / 'templates' / 'aba-api'

class GenericTemplateTests(unittest.TestCase):
    def test_versioned_template_pack_script_matches_metadata(self):
        package = ET.parse(ROOT / "packaging/ABAFramework.GenericApi.Template.csproj")
        version = package.findtext("./PropertyGroup/Version")
        self.assertEqual("0.3.0-preview.2", version)
        script = (ROOT / "scripts/pack-template.sh").read_text()
        self.assertIn("PackageVersion=" + version, script)
        self.assertIn("-p:PackageVersion=" + version, script)
        self.assertNotIn("/p:PackageVersion=", script)
        self.assertIn("ABAFramework.GenericApi.Template." + version + ".nupkg", script)
        self.assertIn('PACKAGE_ID="ABAFramework.GenericApi.Template"', script)
        self.assertIn('dotnet new uninstall "$PACKAGE_ID"', script)
        self.assertIn('dotnet new uninstall 2>/dev/null | grep -Fq "$PACKAGE_ID"', script)

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
                     'WolverineFx.SqlServer', 'WolverineFx.RabbitMQ', 'WolverineFx.Kafka'):
            self.assertIn(name, package)
        program = (TEMPLATE / 'src/Aba.Starter.Api/Program.cs').read_text()
        self.assertIn('builder.AddAbaPlatform()', program)

    def test_optional_rabbit_transport_is_guarded_and_has_real_broker_cases(self):
        options = (TEMPLATE / 'src/Aba.Starter.Platform/Messaging/AbaMessagingOptions.cs').read_text()
        service = (TEMPLATE / 'src/Aba.Starter.Platform/Messaging/AbaWolverineServices.cs').read_text()
        platform = (TEMPLATE / 'src/Aba.Starter.Platform/AbaPlatformSetup.cs').read_text()
        fast_tests = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/AbaMessagingOptionsTests.cs').read_text()
        broker_tests = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/RabbitMqIntegrationTests.cs').read_text()
        kafka_tests = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/KafkaIntegrationTests.cs').read_text()
        workflow = (TEMPLATE / '.github/workflows/integration.yml').read_text()
        compose = (ROOT / 'infra/compose.platform.yaml').read_text()
        starter_compose = (TEMPLATE / 'compose.yaml').read_text()
        self.assertIn('RabbitMQ', options)
        self.assertIn('Production RabbitMQ requires amqps://', options)
        self.assertIn('Production Kafka requires SASL_SSL', options)
        self.assertIn('PublisherConfirmationsEnabled = true', service)
        self.assertIn('PublisherConfirmationTrackingEnabled = true', service)
        self.assertIn('Action<WolverineOptions>? configureMessaging', platform)
        self.assertIn('ProductionRequiresTlsAndNonGuestCredentials', fast_tests)
        self.assertIn('RabbitMqDurableDeliveryHandlesDuplicatePayloadOnce', broker_tests)
        self.assertIn('BrokerOutageDrill', broker_tests)
        self.assertIn('job.services.rabbitmq.id', workflow)
        self.assertIn('Category=BrokerOutageDrill', workflow)
        outage_step = workflow.split(
            '- name: Pause and recover only the disposable CI RabbitMQ service', 1
        )[1].split('- name: Destructive drills only against disposable local CI databases', 1)[0]
        self.assertEqual(outage_step.count('        run:'), 1)
        self.assertEqual(outage_step.count("--logger 'console;verbosity=normal'"), 1)
        self.assertIn('rabbitmq:4.4.0-management-alpine', compose)
        self.assertIn('127.0.0.1:56729:5672', compose)
        self.assertIn('WolverineFx.Kafka', (TEMPLATE / 'src/Aba.Starter.Platform/Aba.Starter.Platform.csproj').read_text())
        self.assertIn('KafkaDurableDeliveryHandlesDuplicatePayloadOnce', kafka_tests)
        self.assertIn('apache/kafka:4.4.0', workflow)
        self.assertIn('ABA_TEST_KAFKA', workflow)
        self.assertIn('KAFKA', compose)
        self.assertIn('127.0.0.1:9092:9092', compose)
        self.assertIn('profiles: [kafka]', starter_compose)
        self.assertIn('apache/kafka:4.4.0', starter_compose)

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
        for provider in ("postgres", "sqlserver", "redis", "rabbitmq"):
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

    def test_generated_supply_chain_gate_is_present(self):
        source = (TEMPLATE / 'tools/supply_chain.py').read_text()
        cases = (TEMPLATE / 'tools/test_supply_chain.py').read_text()
        root_ci = (ROOT / '.github/workflows/verify.yml').read_text()
        template_ci = (TEMPLATE / '.github/workflows/ci.yml').read_text()
        self.assertIn("SPDX-2.3", source)
        self.assertIn("Known vulnerable NuGet packages", source)
        self.assertIn("test_transitive_vulnerability_is_rejected", cases)
        for phrase in ("--vulnerable", "--include-transitive", "--output-version 1",
                       "tools/supply_chain.py", "tools/test_supply_chain.py"):
            self.assertIn(phrase, root_ci)
            self.assertIn(phrase, template_ci)

    def test_external_integration_project_is_scanned_independently(self):
        tool = (TEMPLATE / 'tools/supply_chain.py').read_text()
        tests = (TEMPLATE / 'tools/test_supply_chain.py').read_text()
        workflows = [
            (ROOT / '.github/workflows/verify.yml').read_text(),
            (TEMPLATE / '.github/workflows/ci.yml').read_text()
        ]
        self.assertIn('def single_project(', tool)
        self.assertIn('test_single_integration_project_is_supported', tests)
        for workflow in workflows:
            self.assertIn('--project', workflow)
            self.assertIn('IntegrationTests.csproj', workflow)
            self.assertIn('integration-audit.json', workflow)
            self.assertIn('integration.spdx.json', workflow)

    def test_native_recovery_drills_are_opt_in_ci_only(self):
        path = TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/BackupRestoreDrillTests.cs'
        self.assertTrue(path.is_file())
        source = path.read_text()
        for evidence in ('pg_dump', 'pg_restore', 'BACKUP DATABASE', 'RESTORE DATABASE',
                         'RequireDisposableFixture', 'Category", "RecoveryDrill',
                         'ABA_ENABLE_DESTRUCTIVE_CI_DRILLS', 'postgres:16-alpine'):
            self.assertIn(evidence, source)
        external = (ROOT / 'scripts/verify-platform-services.sh').read_text()
        task = (TEMPLATE / 'tools/aba_task.py').read_text()
        ci = (ROOT / '.github/workflows/verify.yml').read_text()
        generated_ci = (TEMPLATE / '.github/workflows/integration.yml').read_text()
        self.assertIn('ABA_TEST_KAFKA', external)
        self.assertIn('ABA_TEST_KAFKA', task)
        self.assertIn('apache/kafka:4.4.0', ci)
        self.assertIn('apache/kafka:4.4.0', generated_ci)
        self.assertIn("Category!=RecoveryDrill", external)
        self.assertIn("Category!=RecoveryDrill", task)
        for workflow in (ci, generated_ci):
            self.assertIn("postgres:16-alpine", workflow)
            self.assertIn("ABA_ENABLE_DESTRUCTIVE_CI_DRILLS", workflow)

    def test_release_image_scan_and_source_attestation_have_enforced_gates(self):
        root = (ROOT / '.github/workflows/verify.yml').read_text()
        generated = (TEMPLATE / '.github/workflows/ci.yml').read_text()
        for workflow in (root, generated):
            self.assertIn('container-security:', workflow)
            self.assertIn('ed142fd0673e97e23eac54620cfb913e5ce36c25', workflow)
            self.assertIn('exit-code: ', workflow)
            self.assertIn('severity: CRITICAL', workflow)
            self.assertNotIn('continue-on-error: true', workflow)
        for required in ('template-provenance:', 'artifact-metadata: write',
                         'attestations: write', 'id-token: write',
                         '1e69f48acb82d1966a394da916b4c1698aa569d6',
                         'SHA256SUMS'):
            self.assertIn(required, root)
        verifier = (ROOT / 'scripts/verify_attested_artifact.sh').read_text()
        self.assertEqual(2, root.count('bash scripts/verify_attested_artifact.sh'))
        for required in ('gh attestation verify "$artifact"',
                         '--source-ref refs/heads/main --source-digest "$GITHUB_SHA"',
                         'gh attestation verify "$mutated"',
                         'wrong_sha=', 'altered-after-scan'):
            self.assertIn(required, verifier)
        self.assertIn('needs:', root)

    def test_database_rls_policies_and_live_sql_negative_tests_are_bundled(self):
        code = (TEMPLATE / 'src/Aba.Starter.Platform/Persistence/AbaTenantRlsPolicies.cs').read_text()
        live = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/RowLevelSecurityTests.cs').read_text()
        tests = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/TenantRlsPolicyTests.cs').read_text()
        adr = (TEMPLATE / 'docs/decisions/ADR-005-tenant-row-security.md').read_text()
        for text in ('FORCE ROW LEVEL SECURITY', 'SESSION_CONTEXT',
                     'ADD FILTER PREDICATE', 'AFTER INSERT', 'AFTER UPDATE',
                     'AbaTenantSqlScope'):
            self.assertIn(text, code)
        self.assertIn('ExecuteUpdateAsync', live)
        self.assertIn('ExecuteDeleteAsync', live)
        self.assertIn('PostgresRequiresForcedRls', tests)
        self.assertIn('non-superuser', adr)

    def test_opt_in_audit_and_audited_deadletter_replay_are_bundled(self):
        ledger = (TEMPLATE / 'src/Aba.Starter.Platform/Persistence/AbaAuditLedger.cs').read_text()
        operator = (TEMPLATE / 'src/Aba.Starter.Platform/Messaging/AbaDeadLetterAdministration.cs').read_text()
        migration = (TEMPLATE / 'src/Aba.Starter.Platform/Persistence/Migrations/202611130001_AuditLedger.cs').read_text()
        for expected in ('HMACSHA256', 'IsolationLevel.Serializable',
                         'MicrosecondUtcNow', 'VerifyAsync', 'AppendAsync'):
            self.assertIn(expected, ledger)
        for expected in ('deadletters:inspect', 'deadletters:replay',
                         'DeadLetterEnvelopeQuery', 'ReplayAsync',
                         'AbaAuditLedger', 'PermissionClaim'):
            self.assertIn(expected, operator)
        self.assertIn('aba_audit_records', migration)
        self.assertNotIn('MapGet(', operator)
        for path in ('docs/decisions/ADR-006-audit-ledger.md',
                     'docs/decisions/ADR-007-wolverine-deadletter-operator.md',
                     'docs/operations/audit-deadletters.md'):
            self.assertTrue((TEMPLATE / path).is_file())

    def test_crash_ack_window_probe_is_idempotent_and_marker_gated(self):
        code = (TEMPLATE / 'src/Aba.Starter.Platform/Messaging/AbaDurabilityProbe.cs').read_text()
        tests = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/AbruptCrashWindowTests.cs').read_text()
        for phrase in ('AbaCrashAckProbeHandler', 'aba-crash-effect',
                       'aba-crash-attempt-', 'ABA_CI_CRASH_ACK_MARKER',
                       'ABA_ENABLE_DESTRUCTIVE_CI_DRILLS'):
            self.assertIn(phrase, code)
        for phrase in ('"preack"', 'effect-committed-before-ack',
                       'ReceiptCountAsync'):
            self.assertIn(phrase, tests)

    def test_abrupt_sigkill_crash_drills_are_explicitly_gated(self):
        code = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/AbruptCrashWindowTests.cs').read_text()
        script = (ROOT / 'scripts/verify-platform-services.sh').read_text()
        task = (TEMPLATE / 'tools/aba_task.py').read_text()
        workflow = (TEMPLATE / '.github/workflows/integration.yml').read_text()
        for required in ('Kill(entireProcessTree: true)',
                         'CrashChildWorker', 'ABA_ENABLE_DESTRUCTIVE_CI_DRILLS',
                         'Category", "CrashDrill', 'aba-sigkill-uncommitted',
                         'AbaDurabilityProbe'):
            self.assertIn(required, code)
        self.assertIn("Category!=RecoveryDrill&Category!=CrashDrill", script)
        self.assertIn("Category!=RecoveryDrill&Category!=CrashDrill", task)
        self.assertIn("Category=CrashDrill", workflow)
        self.assertIn("Category=CrashDrill", script)

    def test_real_redis_restart_is_isolated_and_fail_closed(self):
        source = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/RedisRestartDrillTests.cs').read_text()
        root_ci = (ROOT / '.github/workflows/verify.yml').read_text()
        generated_ci = (TEMPLATE / '.github/workflows/integration.yml').read_text()
        script = (ROOT / 'scripts/verify-platform-services.sh').read_text()
        task = (TEMPLATE / 'tools/aba_task.py').read_text()
        for marker in ('RequireCiService', 'DockerAsync("stop"', 'DockerAsync("start"',
                       'ABA_ENABLE_REDIS_RESTART_DRILL', 'ABA_TEST_REDIS_CONTAINER_ID',
                       'CacheOutagePolicy.FailClosed', 'UseAuthoritativeSource'):
            self.assertIn(marker, source)
        for workflow in (root_ci, generated_ci):
            self.assertIn('  redis-restart:', workflow)
            self.assertIn('job.services.redis.id', workflow)
            self.assertIn("Category=RedisRestartDrill", workflow)
        for value in (script, task):
            self.assertIn('Category!=CrashDrill&Category!=RedisRestartDrill', value)

    def test_opt_in_distributed_cache_coordination_is_bounded_and_fail_closed(self):
        options = (TEMPLATE / 'src/Aba.Starter.Platform/Caching/AbaCacheDistributedCoordinationOptions.cs').read_text()
        coordinator = (TEMPLATE / 'src/Aba.Starter.Platform/Caching/AbaRedisCacheMissCoordinator.cs').read_text()
        health_check = (TEMPLATE / 'src/Aba.Starter.Platform/Caching/AbaCacheDistributedCoordinationHealthCheck.cs').read_text()
        cache = (TEMPLATE / 'src/Aba.Starter.Platform/Caching/ReadThroughQueryCache.cs').read_text()
        unit_tests = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/AbaCacheDistributedCoordinationTests.cs').read_text()
        integration_tests = (TEMPLATE / 'tests/Aba.Starter.Platform.IntegrationTests/AbaDistributedCacheCoordinationIntegrationTests.cs').read_text()
        adr = (TEMPLATE / 'docs/decisions/ADR-010-cache-singleflight-redis-restart.md').read_text()
        self.assertIn('AbaCacheDistributedCoordinationOptions', options)
        self.assertIn('AbaCacheDistributedCoordinationOptions', coordinator)
        self.assertIn('IAbaCacheMissCoordinator', cache)
        self.assertIn('aba-cache-coordination', (TEMPLATE / 'src/Aba.Starter.Platform/Caching/AbaCachingServices.cs').read_text())
        self.assertIn('VerifyReadinessAsync(cancellationToken)', health_check)
        self.assertIn('Platform:Cache:DistributedCoordination:Enabled', adr)
        for value in (coordinator, integration_tests):
            self.assertIn('MaxConcurrentSourceLoads', value)
            self.assertIn('MaxSourceLoadsPerWindow', value)
        for marker in ('SharedRedisCoordinatesSameMissAcrossIndependentHosts',
                       'SharedRedisEnforcesClusterSourceLoadRateBudgetAcrossHosts',
                       'SharedRedisEnforcesConcurrentSourceLoadBudgetAcrossHosts',
                       'SharedRedisRejectsDifferentCoordinationSettingsInOneNamespace',
                       'DistributedFallbackFailsClosedWhenRedisCannotGrantCoordination'):
            self.assertIn(marker, integration_tests)
        self.assertIn('AbaCacheCoordinationConfigurationMismatchException', coordinator)
        self.assertIn('DistributedCoordinationFailureNeverFallsBackToSource', unit_tests)
        self.assertIn('LeaseLost', cache)

    def test_scanned_image_archive_has_attested_provenance_and_tamper_rejection(self):
        workflow = (ROOT / '.github/workflows/verify.yml').read_text()
        for phrase in ('docker save aba-ci-security:local',
                       'docker load -i aba-runtime-image.tar.gz',
                       "test \"$original\" = \"$restored\"",
                       'subject-path: artifacts/oci-provenance/aba-runtime-image.tar.gz'):
            self.assertIn(phrase, workflow)
        verifier = (ROOT / 'scripts/verify_attested_artifact.sh').read_text()
        for phrase in ('gh attestation verify "$artifact"',
                       'gh attestation verify "$mutated"',
                       '--source-ref refs/heads/main --source-digest "$GITHUB_SHA"',
                       'altered-after-scan',
                       "PASS: signed artifact, source identity and mutation denial verified"):
            self.assertIn(phrase, verifier)
        scan_job = workflow.split('  container-security:', 1)[1].split('  template-provenance:', 1)[0]
        final_job = workflow.split('  template-provenance:', 1)[1]
        self.assertNotIn('actions/attest@', scan_job)
        self.assertIn('needs:', final_job)
        self.assertIn('uses: actions/checkout@v4', final_job)
        self.assertEqual(2, final_job.count('bash scripts/verify_attested_artifact.sh'))
        for upstream in ('governance-and-runtime', 'integration-relational-and-redis',
                         'redis-restart', 'container-startup', 'package-template',
                         'keycloak-oidc', 'supply-chain', 'container-security'):
            self.assertIn('      - ' + upstream, final_job)
        self.assertIn('name: aba-runtime-image-security', final_job)
        self.assertIn('sha256sum --check image-archive-sha256.txt', final_job)

    def test_immutable_mcr_base_manifest_lock_is_enforced_everywhere(self):
        docker = (TEMPLATE / 'Dockerfile').read_text()
        lock = json.loads((TEMPLATE / 'oci-base-lock.json').read_text())
        self.assertEqual(set(lock['images']), {'build', 'runtime'})
        for stage, value in lock['images'].items():
            self.assertIn(value['name'] + '@' + value['digest'] + ' AS ' + stage, docker)
            self.assertRegex(value['digest'], r'^sha256:[0-9a-f]{64}')
            self.assertEqual(len(value['digest']), 71)
        self.assertTrue((TEMPLATE / 'tools/oci_base_lock.py').is_file())
        self.assertTrue((TEMPLATE / 'tools/test_oci_base_lock.py').is_file())
        root_ci = (ROOT / '.github/workflows/verify.yml').read_text()
        generated_ci = (TEMPLATE / '.github/workflows/ci.yml').read_text()
        for source in (root_ci, generated_ci):
            self.assertIn('test_oci_base_lock.py', source)
            self.assertIn('oci_base_lock.py --root', source)

    def test_xunit_and_companion_script_exist(self):
        proj = (TEMPLATE / 'tests/Aba.Starter.Api.Tests/Aba.Starter.Api.Tests.csproj').read_text()
        self.assertIn('PackageReference Include="xunit"', proj)
        self.assertIn('Microsoft.AspNetCore.Mvc.Testing', proj)
        script = (ROOT / 'scripts/verify-starter.sh').read_text()
        for gate in ('dotnet new aba-api', 'dotnet build', 'dotnet test', 'health/live', 'openapi/v1.json'):
            self.assertIn(gate, script)

if __name__ == '__main__':
    unittest.main(verbosity=2)
