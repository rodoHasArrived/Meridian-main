from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_status_delivery_claims.py"
SPEC = importlib.util.spec_from_file_location("check_status_delivery_claims", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["check_status_delivery_claims"] = module
SPEC.loader.exec_module(module)


class StatusDeliveryClaimChecksTests(unittest.TestCase):
    def write_doc(self, root: Path, rel: str, content: str) -> Path:
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_accepts_scoped_paper_claims_with_packet_reference(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(
                root,
                "docs/status/production-status.md",
                """
Status remains in progress with paper-trading evidence.
Latest pass packet: artifacts/provider-validation/_automation/2026-05-17/dk1-pilot-parity-packet.json.
""",
            )
            self.assertEqual([], module.validate_doc(doc))

    def test_rejects_prohibited_live_phrase(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(
                root,
                "docs/status/production-status.md",
                """
This release is production ready for live trading.
Latest pass packet: artifacts/provider-validation/_automation/2026-05-17/dk1-pilot-parity-packet.json.
""",
            )
            errors = module.validate_doc(doc)
            self.assertTrue(any("prohibited" in err for err in errors))


    def test_allows_negated_live_readiness_phrase(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(
                root,
                "docs/status/production-status.md",
                """
This release is not ready for live trading and remains in progress with paper workflow evidence.
Latest pass packet: artifacts/provider-validation/_automation/2026-05-17/dk1-pilot-parity-packet.json.
""",
            )
            self.assertEqual([], module.validate_doc(doc))

    def test_rejects_missing_packet_reference(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(root, "docs/reference/provider-validation-matrix.md", "Paper workflow remains in progress.")
            errors = module.validate_doc(doc)
            self.assertTrue(any("missing latest pass packet reference" in err for err in errors))

    def test_allows_archive_migration_stub_without_current_packet_claim(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(
                root,
                "docs/status/provider-validation-matrix.md",
                """
# Archived Legacy Status Item: provider-validation-matrix

This status file has been migrated to docs/reference/provider-validation-matrix.md.

**Status:** archive-migration-stub
""",
            )
            self.assertEqual([], module.validate_doc(doc))

    def test_default_docs_validate_active_canonical_status_docs(self) -> None:
        self.assertEqual(("docs/reference/provider-validation-matrix.md",), module.DEFAULT_DOCS)

    def test_default_docs_do_not_validate_archive_stubs(self) -> None:
        self.assertNotIn("docs/status/production-status.md", module.DEFAULT_DOCS)
        self.assertNotIn("docs/status/provider-validation-matrix.md", module.DEFAULT_DOCS)

    def test_allows_archive_migration_stub_without_current_packet_claim(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            doc = self.write_doc(
                root,
                "docs/status/provider-validation-matrix.md",
                """
# Archived Legacy Status Item: provider-validation-matrix

This status file has been migrated to docs/reference/provider-validation-matrix.md.

**Status:** archive-migration-stub
""",
            )
            self.assertEqual([], module.validate_doc(doc))


if __name__ == "__main__":
    unittest.main()
