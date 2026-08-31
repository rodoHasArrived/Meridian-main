import unittest
import xml.etree.ElementTree as ET
import os
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
CENTRAL_PACKAGES = REPO_ROOT / "Directory.Packages.props"
BUILD_PROPS = REPO_ROOT / "Directory.Build.props"


class CentralPackageVersionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        root = ET.parse(CENTRAL_PACKAGES).getroot()
        cls.properties = {
            item.tag: item.text
            for item in root.findall("./PropertyGroup/*")
            if item.text is not None
        }
        cls.versions = {
            item.attrib["Include"]: item.attrib["Version"]
            for item in root.findall(".//PackageVersion")
        }

    def test_microsoft_extensions_pins_match_hosting_transitives(self) -> None:
        packages = [
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Json",
            "Microsoft.Extensions.Configuration.Binder",
            "Microsoft.Extensions.Configuration.EnvironmentVariables",
            "Microsoft.Extensions.Configuration.CommandLine",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.Extensions.Caching.Memory",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Logging.Debug",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Options.ConfigurationExtensions",
        ]

        for package in packages:
            with self.subTest(package=package):
                self.assertEqual(self.versions[package], "10.0.7")

    def test_json_stack_pins_match_system_text_json_transitives(self) -> None:
        self.assertEqual(self.versions["System.Text.Json"], "10.0.7")
        self.assertEqual(self.versions["System.IO.Pipelines"], "10.0.7")

    def test_quantconnect_lean_stays_on_net9_compatible_line(self) -> None:
        self.assertEqual(self.properties["QuantConnectLeanVersion"], "2.5.17414")

        packages = [
            "QuantConnect.Lean",
            "QuantConnect.Lean.Engine",
            "QuantConnect.Common",
            "QuantConnect.Indicators",
        ]

        for package in packages:
            with self.subTest(package=package):
                self.assertEqual(self.versions[package], "$(QuantConnectLeanVersion)")
                self.assertNotEqual(self.versions[package], "2.5.17677")

    def test_aspnet_test_host_has_central_pin(self) -> None:
        self.assertEqual(self.versions["Microsoft.AspNetCore.Mvc.Testing"], "9.0.15")
        self.assertEqual(self.versions["Microsoft.AspNetCore.TestHost"], "9.0.15")

    def test_security_transitive_pins_cover_current_audit_fixes(self) -> None:
        self.assertEqual(self.properties["CentralPackageTransitivePinningEnabled"], "true")
        self.assertEqual(self.versions["Snappier"], "1.3.1")

    def test_nuget_audit_suppression_is_advisory_specific(self) -> None:
        """Every suppression must name one advisory and carry its accepted-risk metadata.

        This previously pinned the exact GHSA-xhg6-9j5j-w4vf entry, so removing a suppression
        whose advisory no longer applies — the outcome the policy wants — failed the suite. Assert
        the property that must always hold instead; no suppressions at all is valid and preferred.
        """
        root = ET.parse(BUILD_PROPS).getroot()
        advisory_suppressions = [
            item for item in root.findall(".//MeridianNuGetAuditSuppression")
        ]
        suppressions = [
            item.attrib["Include"]
            for item in root.findall(".//NuGetAuditSuppress")
        ]

        for item in advisory_suppressions:
            include = item.attrib["Include"]
            self.assertRegex(
                include,
                r"^https://github\.com/advisories/GHSA-[\w-]+$",
                "a suppression must name exactly one advisory by URL, never a package or wildcard",
            )
            for field in ("Owner", "Justification", "RatchetPlan"):
                element = item.find(field)
                self.assertIsNotNone(element, f"{include} is missing {field}")
                self.assertTrue(
                    (element.text or "").strip(),
                    f"{include} has an empty {field}",
                )

        # The indirection exists so suppressions can only be added through the documented item,
        # and must stay wired whenever any suppression exists.
        if advisory_suppressions:
            self.assertEqual(["@(MeridianNuGetAuditSuppression)"], suppressions)
        else:
            self.assertEqual([], suppressions)

    def test_all_project_package_references_have_central_versions(self) -> None:
        missing: list[str] = []
        excluded_dirs = {"bin", "obj", "node_modules", ".git"}

        project_paths: list[Path] = []
        for current_root, dirnames, filenames in os.walk(REPO_ROOT):
            dirnames[:] = [name for name in dirnames if name not in excluded_dirs]
            project_paths.extend(
                Path(current_root) / filename
                for filename in filenames
                if filename.endswith("proj") and "." in filename
            )

        for project_path in sorted(project_paths):
            root = ET.parse(project_path).getroot()
            for reference in root.findall(".//PackageReference"):
                package = reference.attrib.get("Include")
                if not package:
                    continue

                if reference.attrib.get("Version") or reference.find("Version") is not None:
                    continue

                if package not in self.versions:
                    relative_path = project_path.relative_to(REPO_ROOT).as_posix()
                    missing.append(f"{relative_path}: {package}")

        self.assertEqual([], missing)


if __name__ == "__main__":
    unittest.main()
