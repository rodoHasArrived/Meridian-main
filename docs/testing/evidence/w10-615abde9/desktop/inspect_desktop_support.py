#!/usr/bin/env python3
"""Inventory existing desktop support; this does not execute .NET/WPF tests."""
from pathlib import Path
import argparse
import hashlib
import json
import platform
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--root", type=Path, help="Isolated checkout at the acceptance candidate")
args = parser.parse_args()
root = Path(subprocess.check_output(
    ["git", "rev-parse", "--show-toplevel"], cwd=args.root or Path(__file__).resolve().parent, text=True
).strip())
candidate = "615abde90001ab33bd6e58e545edc7fce635e254"
actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
if actual != candidate:
    raise SystemExit(f"Candidate mismatch: {actual}")
if subprocess.check_output(["git", "status", "--porcelain", "--", "src", "tests"], cwd=root, text=True).strip():
    raise SystemExit("Candidate source/test files are modified")

files = [
    "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj",
    "src/Meridian.Wpf/Meridian.Wpf.csproj",
    "tests/Meridian.Wpf.Tests/ViewModels/OperationsContinuityViewModelTests.cs",
    "tests/Meridian.Wpf.Tests/ViewModels/AccountingCloseViewModelTests.cs",
    "tests/Meridian.Wpf.Tests/Features/Accounting/AccountingCloseHttpRecoveryTests.cs",
    "tests/Meridian.Wpf.Tests/Features/Accounting/AccountingCloseHttpRecoveryTests.ResponseIsolation.cs",
    "tests/Meridian.Wpf.Tests/ViewModels/MarkFreshnessPresentationTests.cs",
    "src/Meridian.Wpf/Workstation/Models/OperationsContinuityClosePresentation.cs",
    "src/Meridian.Wpf/ViewModels/OperationsContinuityViewModel.cs",
    "src/Meridian.Wpf/Views/OperationsContinuityPage.xaml",
    "src/Meridian.Wpf/Models/MarkFreshnessPresentation.cs",
    "src/Meridian.Wpf/ViewModels/AccountPortfolioViewModel.cs",
    "src/Meridian.Wpf/ViewModels/AggregatePortfolioViewModel.cs",
    "src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs",
    "tests/Meridian.Tests/Application/Accounting/ValuationFreshnessAcceptanceTests.cs",
]
report = {
    "candidate": actual,
    "host_os": platform.system(),
    "python": platform.python_version(),
    "dotnet_available_at_inspection": shutil.which("dotnet") is not None,
    "scope": "Static inventory only; no WPF tests or rendered session executed",
    "files": [],
}
for relative in files:
    contents = (root / relative).read_bytes()
    source = contents.decode("utf-8-sig")
    entry = {"path": relative, "sha256": hashlib.sha256(contents).hexdigest()}
    if relative.endswith(".csproj"):
        tree = ET.fromstring(source)
        entry["platform_properties"] = [
            {"condition": group.get("Condition", "unconditional"),
             "properties": {node.tag: node.text for node in group if node.tag in
                 {"TargetFramework", "EnableDefaultCompileItems", "UseWPF", "EnableFullWpfBuild"}}}
            for group in tree.findall("PropertyGroup")
            if any(node.tag in {"TargetFramework", "EnableDefaultCompileItems", "UseWPF", "EnableFullWpfBuild"} for node in group)
        ]
    if "/Tests/" in relative or relative.startswith("tests/"):
        entry["test_attributes"] = len(re.findall(r"\[(?:Fact|Theory)\]", source))
        entry["methods"] = [
            {"name": match.group(1), "line": source[:match.start()].count("\n") + 1}
            for match in re.finditer(r"public (?:async )?(?:Task|void) (\w+)\(", source)
        ]
    report["files"].append(entry)
report["source_queries"] = []
for pattern, paths in [
    ("ValuationFreshnessPreviewDto|DailyMarkToMarket.*Preview|daily-mark-to-market-preview", ["src/Meridian.Wpf"]),
    ("MarkFreshnessOverride|IMarkOverrideStore|PostgresMarkOverrideStore", ["src", "tests"]),
]:
    command = ["rg", "-l", pattern, *paths]
    result = subprocess.run(command, cwd=root, text=True, capture_output=True)
    if result.returncode not in {0, 1}:
        raise SystemExit(result.stderr)
    report["source_queries"].append({"command": command, "returncode": result.returncode,
                                     "matching_files": result.stdout.splitlines()})
print(json.dumps(report, indent=2))
