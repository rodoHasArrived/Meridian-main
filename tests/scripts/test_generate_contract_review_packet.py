from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "generate_contract_review_packet.py"
SPEC = importlib.util.spec_from_file_location("generate_contract_review_packet", SCRIPT_PATH)
assert SPEC and SPEC.loader
packet_module = importlib.util.module_from_spec(SPEC)
sys.modules["generate_contract_review_packet"] = packet_module
SPEC.loader.exec_module(packet_module)


class ContractReviewPacketTests(unittest.TestCase):
    def test_build_review_packet_marks_no_tracked_changes_ready(self) -> None:
        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=["README.md"],
            patch="",
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=False,
            matrix_migration_note_added=False,
            pr_migration_notes_present=None,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        self.assertEqual(packet["summary"]["contractChangeCategory"], "none")
        self.assertTrue(packet["summary"]["readyForCadenceReview"])
        self.assertEqual(packet["summary"]["trackedSurfaceCount"], 0)

    def test_build_review_packet_marks_additive_contract_change_for_review(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -109,0 +110 @@ public sealed record TradingOperatorReadinessDto(
+    IReadOnlyList<string>? NewWarnings = null);
"""

        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"],
            patch=patch,
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=False,
            matrix_migration_note_added=False,
            pr_migration_notes_present=None,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        self.assertEqual(packet["summary"]["contractChangeCategory"], "additive-or-compatible")
        self.assertTrue(packet["summary"]["readyForCadenceReview"])
        self.assertEqual(packet["trackedSurfaces"][0]["scope"], "workstation-dtos")

    def test_build_review_packet_blocks_breaking_change_without_migration_notes(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Api/UiApiRoutes.cs b/src/Meridian.Contracts/Api/UiApiRoutes.cs
@@ -433 +433,0 @@ public static class UiApiRoutes
-    public const string ExecutionSessionReplay = "/api/execution/sessions/{sessionId}/replay";
"""

        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=["src/Meridian.Contracts/Api/UiApiRoutes.cs"],
            patch=patch,
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=False,
            matrix_migration_note_added=False,
            pr_migration_notes_present=False,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        self.assertEqual(packet["summary"]["contractChangeCategory"], "potential-breaking")
        self.assertFalse(packet["summary"]["readyForCadenceReview"])
        self.assertTrue(any(finding["severity"] == "blocking" for finding in packet["findings"]))

    def test_build_review_packet_accepts_documented_breaking_change_for_review(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Api/UiApiRoutes.cs b/src/Meridian.Contracts/Api/UiApiRoutes.cs
@@ -433 +433,0 @@ public static class UiApiRoutes
-    public const string ExecutionSessionReplay = "/api/execution/sessions/{sessionId}/replay";
"""

        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=[
                "src/Meridian.Contracts/Api/UiApiRoutes.cs",
                "docs/status/contract-compatibility-matrix.md",
            ],
            patch=patch,
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=True,
            matrix_migration_note_added=True,
            pr_migration_notes_present=True,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        self.assertTrue(packet["summary"]["readyForCadenceReview"])
        self.assertEqual(packet["trackedSurfaces"][0]["scope"], "shared-ui-routes")

    def test_build_review_packet_blocks_breaking_change_without_pr_body_check(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Api/UiApiRoutes.cs b/src/Meridian.Contracts/Api/UiApiRoutes.cs
@@ -433 +433,0 @@ public static class UiApiRoutes
-    public const string ExecutionSessionReplay = "/api/execution/sessions/{sessionId}/replay";
"""

        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=[
                "src/Meridian.Contracts/Api/UiApiRoutes.cs",
                "docs/status/contract-compatibility-matrix.md",
            ],
            patch=patch,
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=True,
            matrix_migration_note_added=True,
            pr_migration_notes_present=None,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        self.assertFalse(packet["summary"]["readyForCadenceReview"])
        self.assertTrue(any("PR body was not supplied" in finding["message"] for finding in packet["findings"]))

    def test_render_markdown_includes_review_status_and_surfaces(self) -> None:
        packet = packet_module.build_review_packet(
            base_ref="origin/main",
            head_ref="HEAD",
            changed_files=["src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs"],
            patch="",
            matrix_doc_path="docs/status/contract-compatibility-matrix.md",
            matrix_updated=False,
            matrix_migration_note_added=False,
            pr_migration_notes_present=None,
            generated_at_utc="2026-04-27T00:00:00+00:00",
        )

        markdown = packet_module.render_markdown(packet)

        self.assertIn("Ready for cadence review: true", markdown)
        self.assertIn("src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs", markdown)
        self.assertIn("workstation-endpoints", markdown)


if __name__ == "__main__":
    unittest.main()
