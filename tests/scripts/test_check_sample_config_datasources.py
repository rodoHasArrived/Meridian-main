from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build/scripts/ci/check-sample-config-datasources.py"
SPEC = importlib.util.spec_from_file_location("check_sample_config_datasources", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class SampleConfigDataSourceTests(unittest.TestCase):
    def validate(self, document: dict) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as tmp:
            sample = Path(tmp) / "sample.json"
            sample.write_text(json.dumps(document), encoding="utf-8")
            output = io.StringIO()
            with patch.object(module, "SAMPLE", sample), contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                result = module.main()
            return result, output.getvalue()

    def test_reads_all_streaming_factories_from_the_application_descriptor_catalog(self) -> None:
        self.assertEqual(
            {"synthetic", "ibkr", "alpaca", "polygon", "robinhood", "nyse"},
            module.streaming_source_ids(),
        )
        self.assertIn("alpaca", module.historical_capable_families())
        self.assertIn("ibkr", module.historical_capable_families())
        self.assertNotIn("nyse", module.historical_capable_families())

    def test_configuration_aliases_use_the_shared_identity_map(self) -> None:
        for alias in (" IB ", "ibkr", "interactive-brokers", "alpaca-api", "polygon-io", "nyse-streaming"):
            with self.subTest(alias=alias):
                result, output = self.validate({"DataSource": alias})
                self.assertEqual(0, result, output)

    def test_historical_and_nonproduction_aliases_are_not_streaming_sources(self) -> None:
        for alias in ("yahoo-finance", "template-brokerage", "tradier", "tradestation"):
            with self.subTest(alias=alias):
                result, output = self.validate({"DataSource": alias})
                self.assertEqual(1, result, output)

    def test_unreadable_catalog_fails_closed(self) -> None:
        with patch.object(module, "CATALOG_SOURCE", module.CATALOG_SOURCE.with_name("missing-catalog.cs")):
            result, output = self.validate({"DataSource": "Synthetic"})
        self.assertEqual(1, result)
        self.assertIn("no streaming factories parsed", output)

    def test_unreadable_identity_map_fails_closed(self) -> None:
        with patch.object(module, "IDENTITY_SOURCE", module.IDENTITY_SOURCE.with_name("missing-identity.cs")):
            result, output = self.validate({"DataSource": "Synthetic"})
        self.assertEqual(1, result)
        self.assertIn("no provider aliases parsed", output)

    def test_internal_factories_preserve_activation_and_credential_checks(self) -> None:
        activation = module.backfill_activation()
        self.assertEqual("opt-in", activation["synthetic"])
        self.assertEqual("default-on", activation["yahoo"])
        self.assertEqual("default-on", activation["alpaca"])
        self.assertIn("alpaca", module.credential_gated_families())

        for family, extra, expected in (
            ("Synthetic", {}, "only for an explicit true"),
            ("Synthetic", {"Enabled": False}, "Enabled is false"),
            ("Alpaca", {"Enabled": True}, "credentials are absent"),
        ):
            with self.subTest(family=family, extra=extra):
                result, output = self.validate({
                    "DataSource": "Synthetic",
                    "DataSources": {
                        "Sources": [{"Id": "history", "Provider": family, "Type": "Historical"}],
                        "DefaultHistoricalSourceId": "history",
                    },
                    "Backfill": {"Providers": {family: extra}},
                })
                self.assertEqual(1, result)
                self.assertIn(expected, output)

    def test_positional_nulls_and_metadata_without_factory_do_not_advertise_streaming(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            catalog = Path(tmp) / "catalog.cs"
            catalog.write_text('''
                new("history", null, typeof(History)),
                new("metadata", Streaming: typeof(Stream)),
                new("configured", Streaming: typeof(Stream),
                    Exclusions: [new(nameof(Other), "exclusion")],
                    StreamingFactory: static f => f.CreateStream())
            ];
            ''', encoding="utf-8")
            with patch.object(module, "CATALOG_SOURCE", catalog):
                self.assertEqual({"configured"}, module.streaming_source_ids())
                self.assertEqual({"history"}, module.historical_capable_families())


if __name__ == "__main__":
    unittest.main()
