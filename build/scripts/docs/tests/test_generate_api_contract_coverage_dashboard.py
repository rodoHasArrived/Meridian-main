#!/usr/bin/env python3
"""Regression tests for API contract coverage generation."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from pathlib import PurePosixPath
from pathlib import PureWindowsPath


DOCS_SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DOCS_SCRIPT_DIR))


def load_module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, DOCS_SCRIPT_DIR / filename)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {filename}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


api_contract_coverage = load_module(
    "generate_api_contract_coverage_dashboard_under_test",
    "generate-api-contract-coverage-dashboard.py",
)


class GenerateApiContractCoverageDashboardTests(unittest.TestCase):
    def test_path_sort_key_is_stable_across_platform_path_types(self) -> None:
        for path_type in (PurePosixPath, PureWindowsPath):
            with self.subTest(path_type=path_type.__name__):
                ordered = sorted(
                    [
                        path_type("LedgerEndpoints.JournalAutomation.cs"),
                        path_type("LedgerEndpoints.cs"),
                        path_type("IReportingRunNotifier.cs"),
                        path_type("InvestmentAccountingDtos.cs"),
                    ],
                    key=api_contract_coverage._path_sort_key,
                )
                self.assertEqual(
                    [
                        "InvestmentAccountingDtos.cs",
                        "IReportingRunNotifier.cs",
                        "LedgerEndpoints.cs",
                        "LedgerEndpoints.JournalAutomation.cs",
                    ],
                    [path.name for path in ordered],
                )
                cross_directory = sorted(
                    [
                        path_type("src/Meridian.Application/App.cs"),
                        path_type("src/Meridian/UiServer.cs"),
                    ],
                    key=api_contract_coverage._path_sort_key,
                )
                self.assertEqual(
                    [
                        "src/Meridian/UiServer.cs",
                        "src/Meridian.Application/App.cs",
                    ],
                    [path.as_posix() for path in cross_directory],
                )

    def test_scanners_use_explicit_cross_platform_path_order(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            endpoints = root / "src" / "Meridian.Ui.Shared" / "Endpoints"
            endpoints.mkdir(parents=True)
            (endpoints / "LedgerEndpoints.cs").write_text("", encoding="utf-8")
            (endpoints / "LedgerEndpoints.JournalAutomation.cs").write_text("", encoding="utf-8")

            discovered = api_contract_coverage._iter_files(root / "src", ".cs")
            self.assertEqual(
                [
                    "LedgerEndpoints.cs",
                    "LedgerEndpoints.JournalAutomation.cs",
                ],
                [path.name for path in discovered],
            )

            contracts = root / "src" / "Meridian.Contracts" / "Workstation"
            contracts.mkdir(parents=True)
            (contracts / "InvestmentAccountingDtos.cs").write_text(
                "public sealed record InvestmentAccountingDto;\n",
                encoding="utf-8",
            )
            (contracts / "IReportingRunNotifier.cs").write_text(
                "public sealed class NullReportingRunNotifier { }\n",
                encoding="utf-8",
            )

            scanned_contracts = api_contract_coverage._scan_workstation_contracts(root)
            self.assertEqual(
                ["InvestmentAccountingDto", "NullReportingRunNotifier"],
                [str(contract["name"]) for contract in scanned_contracts],
            )


if __name__ == "__main__":
    unittest.main()
