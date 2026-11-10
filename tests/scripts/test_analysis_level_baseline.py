"""Pin the AnalysisLevel=latest-recommended baseline in .globalconfig.

Directory.Build.props reports the SDK's recommended analyzer rules for C# projects. Rules that
already had violations when that was enabled are held at ``suggestion`` in ``.globalconfig``.
The baseline may only shrink: fix a rule's violations, then delete its line there and its entry
in ``ALLOWED_BASELINE`` below.
"""
from __future__ import annotations

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SEVERITY = re.compile(r"^dotnet_diagnostic\.([A-Za-z]+\d+)\.severity\s*=\s*(\S+)\s*$")

ALLOWED_BASELINE = frozenset({
    "CA1000",
    "CA1001",
    "CA1036",
    "CA1051",
    "CA1068",
    "CA1070",
    "CA1304",
    "CA1305",
    "CA1310",
    "CA1311",
    "CA1507",
    "CA1510",
    "CA1512",
    "CA1513",
    "CA1707",
    "CA1708",
    "CA1711",
    "CA1715",
    "CA1716",
    "CA1720",
    "CA1725",
    "CA1805",
    "CA1806",
    "CA1816",
    "CA1822",
    "CA1825",
    "CA1826",
    "CA1827",
    "CA1829",
    "CA1835",
    "CA1838",
    "CA1845",
    "CA1846",
    "CA1847",
    "CA1848",
    "CA1850",
    "CA1852",
    "CA1854",
    "CA1859",
    "CA1860",
    "CA1861",
    "CA1862",
    "CA1863",
    "CA1865",
    "CA1866",
    "CA1869",
    "CA1872",
    "CA1873",
    "CA1875",
    "CA2012",
    "CA2016",
    "CA2201",
    "CA2208",
    "CA2211",
    "CA2219",
    "CA2241",
    "CA2249",
    "CA2254",
    "CA2263",
    "CA5351",
})


class AnalysisLevelBaselineTests(unittest.TestCase):
    def test_csharp_projects_use_latest_recommended(self) -> None:
        root = ET.parse(REPO_ROOT / "Directory.Build.props").getroot()
        group = next(g for g in root.findall("PropertyGroup") if g.find("AnalysisLevel") is not None)
        self.assertEqual(group.findtext("AnalysisLevel"), "latest-recommended")
        self.assertEqual(group.attrib["Condition"], "'$(MSBuildProjectExtension)' == '.csproj'")

    def test_baseline_only_shrinks_and_never_disables_a_rule(self) -> None:
        lines = (REPO_ROOT / ".globalconfig").read_text(encoding="utf-8").splitlines()
        self.assertEqual(lines[0], "is_global = true")
        entries = []
        for line in lines:
            if line.startswith("dotnet_diagnostic."):
                match = SEVERITY.match(line)
                self.assertIsNotNone(match, f"Unparseable severity line (no inline comments): {line!r}")
                entries.append(match.groups())
        rules = [rule for rule, _ in entries]
        self.assertEqual(len(rules), len(set(rules)), "A rule is listed twice")
        for rule, severity in entries:
            self.assertEqual(severity, "suggestion", f"{rule} must stay visible as a suggestion, not {severity}")
        self.assertLessEqual(set(rules), ALLOWED_BASELINE, "Do not add rules to the analyzer baseline")
        self.assertEqual(set(rules), ALLOWED_BASELINE, "Remove fixed rules from ALLOWED_BASELINE too")


if __name__ == "__main__":
    unittest.main()
