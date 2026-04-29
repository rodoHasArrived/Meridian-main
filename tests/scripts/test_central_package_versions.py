import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
CENTRAL_PACKAGES = REPO_ROOT / "Directory.Packages.props"


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


if __name__ == "__main__":
    unittest.main()
