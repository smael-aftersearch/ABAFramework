#!/usr/bin/env python3
"""Regression tests for the generated project's portable ABA task workflow."""
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("aba_task", TOOLS / "aba_task.py")
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
context_spec = importlib.util.spec_from_file_location("new_context", TOOLS / "new_context.py")
assert context_spec and context_spec.loader
context_module = importlib.util.module_from_spec(context_spec)
context_spec.loader.exec_module(context_module)


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

    def test_evidence_redacts_connection_strings_and_password_fragments(self):
        previous = os.environ.get("ABA_TEST_POSTGRESQL")
        previous_rabbit = os.environ.get("ABA_TEST_RABBITMQ")
        previous_kafka_password = os.environ.get("Platform__Messaging__Kafka__SaslPassword")
        os.environ["ABA_TEST_POSTGRESQL"] = "Host=localhost;Password=DontLeakMe;"
        os.environ["ABA_TEST_RABBITMQ"] = "amqps://aba:RabbitDontLeakMe@broker.example.test/v1"
        os.environ["Platform__Messaging__Kafka__SaslPassword"] = "KafkaDontLeakMe"
        try:
            text = module.redact_output(
                "Database failed: Host=localhost;Password=DontLeakMe; " +
                "Broker failed: amqps://aba:RabbitDontLeakMe@broker.example.test/v1 " +
                "Kafka failed: KafkaDontLeakMe other Password=SeparateSecret")
            self.assertNotIn("DontLeakMe", text)
            self.assertNotIn("RabbitDontLeakMe", text)
            self.assertNotIn("KafkaDontLeakMe", text)
            self.assertNotIn("SeparateSecret", text)
            self.assertIn("[REDACTED]", text)
        finally:
            if previous is None:
                os.environ.pop("ABA_TEST_POSTGRESQL", None)
            else:
                os.environ["ABA_TEST_POSTGRESQL"] = previous
            if previous_rabbit is None:
                os.environ.pop("ABA_TEST_RABBITMQ", None)
            else:
                os.environ["ABA_TEST_RABBITMQ"] = previous_rabbit
            if previous_kafka_password is None:
                os.environ.pop("Platform__Messaging__Kafka__SaslPassword", None)
            else:
                os.environ["Platform__Messaging__Kafka__SaslPassword"] = previous_kafka_password

    def test_recovery_drills_are_excluded_from_implicit_full_verification(self):
        source = (TOOLS / "aba_task.py").read_text()
        self.assertIn("Category!=RecoveryDrill", source)
        self.assertIn("Category!=BrokerOutageDrill", source)
        self.assertIn("Category!=BrokerCrashDrill", source)
        self.assertIn('"ABA_TEST_RABBITMQ"', source)
        self.assertIn('"ABA_TEST_KAFKA"', source)

    def test_static_verification_runs_real_governance(self):
        outcome = module.verify("static")
        self.assertEqual("PASS", outcome["status"])
        self.assertTrue(outcome["steps"])
        self.assertIn("new_context.py", " ".join(outcome["steps"][0]["command"]))


class ContractSurfaceTests(unittest.TestCase):
    def test_empty_scaffold_does_not_invent_contracts(self):
        with tempfile.TemporaryDirectory() as temporary:
            context_module.validate_contract_surface(
                "Catalog", Path(temporary), [], [], [])

    def test_owner_approved_versioned_contract_matches_metadata(self):
        with tempfile.TemporaryDirectory() as temporary:
            contracts = Path(temporary)
            (contracts / "InventoryChangedV1.cs").write_text(
                "namespace Product.Inventory.Contracts;\n"
                "public sealed record InventoryChangedV1(string Sku);\n",
                encoding="utf-8",
            )
            context_module.validate_contract_surface(
                "Inventory", contracts, ["InventoryChangedV1"],
                ["InventoryChangedV1"], ["Identity.UserRegisteredV1"])

    def test_unversioned_public_type_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            contracts = Path(temporary)
            (contracts / "InventoryChanged.cs").write_text(
                "public record InventoryChanged(string Sku);\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unversioned public type"):
                context_module.validate_contract_surface(
                    "Inventory", contracts, ["InventoryChanged"], [], [])

    def test_public_contract_must_be_recorded_as_owned(self):
        with tempfile.TemporaryDirectory() as temporary:
            contracts = Path(temporary)
            (contracts / "InventoryChangedV1.cs").write_text(
                "public record InventoryChangedV1(string Sku);\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "declarations differ from ownership"):
                context_module.validate_contract_surface(
                    "Inventory", contracts, [], [], [])

    def test_publish_must_reference_an_owned_contract(self):
        with tempfile.TemporaryDirectory() as temporary:
            contracts = Path(temporary)
            with self.assertRaisesRegex(ValueError, "absent from its owned Contracts project"):
                context_module.validate_contract_surface(
                    "Inventory", contracts, [], ["InventoryChangedV1"], [])

    def test_consumed_contract_requires_explicit_version(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaisesRegex(ValueError, "explicitly versioned"):
                context_module.validate_contract_surface(
                    "Inventory", Path(temporary), [], [], ["Identity.UserRegistered"])


class ContextScaffoldingTests(unittest.TestCase):
    def test_generated_projects_are_grouped_under_modules_and_tests(self):
        solution = ET.fromstring(
            '<Solution><Folder Name="/API/"><Project Path="src/Pilot.Api/Pilot.Api.csproj" />'
            '</Folder></Solution>')
        context_module.add_solution_project(
            solution, "/Modules/", "src/Modules/Catalog/Domain/Pilot.Catalog.Domain.csproj")
        context_module.add_solution_project(
            solution, "/Tests/", "tests/Pilot.Catalog.Tests/Pilot.Catalog.Tests.csproj")

        grouped = {
            folder.attrib["Name"]: {project.attrib["Path"] for project in folder.iter("Project")}
            for folder in solution.findall("Folder")
        }
        self.assertIn("src/Modules/Catalog/Domain/Pilot.Catalog.Domain.csproj",
                      grouped["/Modules/"])
        self.assertIn("tests/Pilot.Catalog.Tests/Pilot.Catalog.Tests.csproj",
                      grouped["/Tests/"])
        with self.assertRaisesRegex(ValueError, "already registered"):
            context_module.add_solution_project(
                solution, "/Tests/", "tests/Pilot.Catalog.Tests/Pilot.Catalog.Tests.csproj")
        with self.assertRaisesRegex(ValueError, "start and end"):
            context_module.add_solution_project(solution, "Tests", "tests/Invalid.csproj")

    def test_with_db_scaffold_supports_design_time_migrations_without_inventing_schema(self):
        files, _ = context_module.files_for("ContextProbe", "Pilot.QuickStart", with_db=True)
        factory_path = Path(
            "src/Modules/ContextProbe/Infrastructure/Persistence/ContextProbeDbContextFactory.cs")
        context_path = Path(
            "src/Modules/ContextProbe/Infrastructure/Persistence/ContextProbeDbContext.cs")
        persistence_readme_path = Path(
            "src/Modules/ContextProbe/Infrastructure/Persistence/README.md")
        infrastructure_path = Path(
            "src/Modules/ContextProbe/Infrastructure/Pilot.QuickStart.ContextProbe.Infrastructure.csproj")
        tests_project_path = Path(
            "tests/Pilot.QuickStart.ContextProbe.Tests/Pilot.QuickStart.ContextProbe.Tests.csproj")
        factory = files[factory_path]
        context = files[context_path]
        persistence_readme = files[persistence_readme_path]
        infrastructure = files[infrastructure_path]
        tests_project = files[tests_project_path]
        platform_project = context_module.ROOT / "src/Aba.Starter.Platform/Aba.Starter.Platform.csproj"
        platform_xml = ET.parse(platform_project)
        design_version = next(
            reference.attrib["Version"]
            for reference in platform_xml.findall(".//PackageReference")
            if reference.attrib.get("Include") == "Microsoft.EntityFrameworkCore.Design")

        self.assertIn("IDesignTimeDbContextFactory<ContextProbeDbContext>", factory)
        self.assertIn('GetEnvironmentVariable("ABA_DB_PROVIDER")', factory)
        self.assertIn('GetEnvironmentVariable("ABA_DB_CONNECTION")', factory)
        self.assertIn("options.UseNpgsql(connection)", factory)
        self.assertIn("options.UseSqlServer(connection)", factory)
        self.assertIn(
            f'Microsoft.EntityFrameworkCore.Design" Version="{design_version}"', infrastructure)
        self.assertIn("<PrivateAssets>all</PrivateAssets>", infrastructure)
        self.assertIn(
            f'Microsoft.EntityFrameworkCore.Design" Version="{design_version}"', tests_project)
        self.assertNotIn("DbSet<", context)
        self.assertIn("--startup-project", persistence_readme)
        self.assertIn("ABA_DB_PROVIDER", persistence_readme)
        self.assertFalse(any("/Migrations/" in str(path).replace("\\", "/")
                             for path in files))

    def test_without_db_scaffold_does_not_add_design_time_database_tooling(self):
        files, _ = context_module.files_for("ContextProbe", "Pilot.QuickStart")
        paths = [str(path).replace("\\", "/") for path in files]
        self.assertFalse(any(path.endswith("ContextProbeDbContextFactory.cs") for path in paths))
        self.assertFalse(any("Microsoft.EntityFrameworkCore.Design" in content
                             for content in files.values()))


if __name__ == "__main__":
    unittest.main(verbosity=2)
