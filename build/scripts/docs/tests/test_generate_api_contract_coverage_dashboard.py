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


    def _build_repo_with_endpoint(self, root: Path) -> None:
        endpoints = root / "src" / "Meridian.Ui.Shared" / "Endpoints"
        endpoints.mkdir(parents=True)
        (endpoints / "FirstRunEndpoints.cs").write_text(
            'app.MapGet("/api/auth/desktop-launch/{ticket}", Handler);\n',
            encoding="utf-8",
        )
        (root / "docs").mkdir()

    def test_generated_reports_do_not_count_as_documentation(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._build_repo_with_endpoint(root)
            # A prior dashboard run lists every scanned route in its own report.
            (root / "docs" / "status").mkdir()
            (root / "docs" / "status" / "api-contract-coverage-dashboard.md").write_text(
                "| GET | `/api/auth/desktop-launch/{ticket}` | gap |\n",
                encoding="utf-8",
            )

            payload = api_contract_coverage.build_dashboard(root)

            self.assertEqual(0, payload["summary"]["documented_endpoint_count"])
            self.assertEqual(1, payload["summary"]["undocumented_endpoint_count"])

    def test_reference_docs_count_as_documentation(self) -> None:
        # The counterweight to the generated-report case above: a hand-written document does
        # count. It has to be reference documentation now -- the corpus became an allowlist of
        # contract-describing roots in #2703, because subtracting prose never converged.
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._build_repo_with_endpoint(root)
            (root / "docs" / "reference").mkdir()
            (root / "docs" / "reference" / "desktop-launch-api.md").write_text(
                "The desktop launch handshake calls `/api/auth/desktop-launch/{ticket}`.\n",
                encoding="utf-8",
            )

            payload = api_contract_coverage.build_dashboard(root)

            self.assertEqual(1, payload["summary"]["documented_endpoint_count"])
            self.assertEqual(0, payload["summary"]["undocumented_endpoint_count"])

    def test_a_non_reference_root_does_not_count_as_documentation(self) -> None:
        # The same sentence in a development guide no longer moves the score. Naming a route is
        # not describing it, and no syntactic rule reliably told the two apart (#2703).
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._build_repo_with_endpoint(root)
            (root / "docs" / "development").mkdir()
            (root / "docs" / "development" / "first-run.md").write_text(
                "The desktop launch handshake calls `/api/auth/desktop-launch/{ticket}`.\n",
                encoding="utf-8",
            )

            payload = api_contract_coverage.build_dashboard(root)

            self.assertEqual(0, payload["summary"]["documented_endpoint_count"])
            self.assertEqual(1, payload["summary"]["undocumented_endpoint_count"])

    def test_coverage_is_stable_when_a_prior_report_is_present(self) -> None:
        """The score must not depend on whether a previous run left a report behind."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._build_repo_with_endpoint(root)
            (root / "docs" / "development").mkdir()
            (root / "docs" / "development" / "first-run.md").write_text(
                "Calls `/api/auth/desktop-launch/{ticket}`.\n",
                encoding="utf-8",
            )

            first = api_contract_coverage.build_dashboard(root)

            (root / "docs" / "status").mkdir()
            (root / "docs" / "status" / "api-contract-coverage-dashboard.md").write_text(
                "| GET | `/api/auth/desktop-launch/{ticket}` | documented |\n",
                encoding="utf-8",
            )
            (root / "docs" / "generated" / "workflow-command-reference.md").parent.mkdir()
            (root / "docs" / "generated" / "workflow-command-reference.md").write_text(
                "`/api/auth/desktop-launch/{ticket}`\n",
                encoding="utf-8",
            )

            second = api_contract_coverage.build_dashboard(root)

            self.assertEqual(first["score_percent"], second["score_percent"])


if __name__ == "__main__":
    unittest.main()
