"""Pin the nullable warnings-as-errors ratchet in Directory.Build.props.

Nullable-reference warnings are build errors in every C# project except a baseline of projects
that still emitted them when the ratchet was introduced. The baseline may only shrink: fix a
project's warnings, then remove it from both the props file and ``ALLOWED_BASELINE`` below.
"""
from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROPS = REPO_ROOT / "Directory.Build.props"

ALLOWED_BASELINE = frozenset({
    "Meridian.Application",
    "Meridian.Contracts",
    "Meridian.DataIntegration",
    "Meridian.Execution",
    "Meridian.FinancialOperations",
    "Meridian.FundStructure.Tests",
    "Meridian.Infrastructure",
    "Meridian.Launcher",
    "Meridian.LifecycleSupervisor",
    "Meridian.LifecycleSupervisor.Tests",
    "Meridian.Reporting",
    "Meridian.Setup",
    "Meridian.Setup.Tests",
    "Meridian.Storage",
    "Meridian.Tests",
    "Meridian.Ui.Shared",
    "Meridian.Wpf",
    "Meridian.Wpf.Tests",
})


class NullableWarningRatchetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.root = ET.parse(PROPS).getroot()

    def test_baseline_only_shrinks_and_names_real_projects(self) -> None:
        raw = self.root.findtext(".//MeridianNullableWarningBaseline") or ""
        self.assertTrue(raw.startswith(";") and raw.endswith(";"), "Baseline must be ;-delimited on both ends")
        baseline = [name for name in raw.split(";") if name]
        self.assertEqual(len(baseline), len(set(baseline)), "Baseline lists a project twice")
        self.assertLessEqual(set(baseline), ALLOWED_BASELINE, "Do not add projects to the nullable baseline")
        self.assertEqual(set(baseline), ALLOWED_BASELINE, "Remove fixed projects from ALLOWED_BASELINE too")
        projects = {path.stem for path in REPO_ROOT.glob("src/**/*.csproj")}
        projects |= {path.stem for path in REPO_ROOT.glob("tests/**/*.csproj")}
        self.assertLessEqual(set(baseline), projects, "Baseline names a project that no longer exists")

    def test_nullable_warnings_are_errors_outside_the_baseline(self) -> None:
        element = self.root.find(".//WarningsAsErrors")
        self.assertIsNotNone(element)
        self.assertEqual(element.text, "$(WarningsAsErrors);nullable")
        condition = element.attrib["Condition"]
        self.assertIn("'$(MSBuildProjectExtension)' == '.csproj'", condition)
        self.assertIn("!$(MeridianNullableWarningBaseline.Contains(';$(MeridianNullableRatchetProjectName);'))", condition)
        # WPF markup compilation builds '<name>_<random>_wpftmp'; it must resolve to the real project.
        name = self.root.findtext(".//MeridianNullableRatchetProjectName") or ""
        self.assertIn("'$(MSBuildProjectName)', '_[A-Za-z0-9]+_wpftmp$', ''", name)


if __name__ == "__main__":
    unittest.main()
