from __future__ import annotations

import importlib.util
from contextlib import contextmanager
import os
import tempfile
import sys
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_contract_compatibility_gate.py"
SPEC = importlib.util.spec_from_file_location("check_contract_compatibility_gate", SCRIPT_PATH)
assert SPEC and SPEC.loader
gate = importlib.util.module_from_spec(SPEC)
sys.modules["check_contract_compatibility_gate"] = gate
SPEC.loader.exec_module(gate)


@contextmanager
def governance_dto_fixture(body: str):
    # Keep real file reads and repository-relative contract paths without writing
    # fixtures into the checkout or depending on the caller's working directory.
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        relative_path = Path("src/Meridian.Contracts/Workstation/GovernanceInstrumentContractDto.cs")
        fixture = root / relative_path
        fixture.parent.mkdir(parents=True)
        fixture.write_text(body, encoding="utf-8")
        previous_directory = Path.cwd()
        try:
            os.chdir(root)
            yield relative_path.as_posix()
        finally:
            os.chdir(previous_directory)


class ContractCompatibilityGateTests(unittest.TestCase):
    def test_is_tracked_includes_shared_ui_route_constants(self) -> None:
        self.assertTrue(gate.is_tracked("src/Meridian.Contracts/Api/UiApiRoutes.cs"))


    def test_patch_has_breaking_removal_detects_route_constant_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Api/UiApiRoutes.cs b/src/Meridian.Contracts/Api/UiApiRoutes.cs
@@ -433 +433,0 @@ public static class UiApiRoutes
-    public const string ExecutionSessionReplay = "/api/execution/sessions/{sessionId}/replay";
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_detects_route_alias_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Api/UiApiRoutes.cs b/src/Meridian.Contracts/Api/UiApiRoutes.cs
@@ -430 +430,0 @@ public static class UiApiRoutes
-    public static readonly string ExecutionManualOverrideClear = ExecutionControlsManualOverrideClear;
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_detects_record_constructor_parameter_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -102 +102,0 @@ public sealed record TradingOperatorReadinessDto(
-    TradingTrustGateReadinessDto TrustGate,
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_detects_json_named_record_parameter_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs b/src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs
@@ -151 +151,0 @@ public sealed record PortfolioSummary(
-    [property: JsonPropertyName("accountScopeId")] string? AccountScopeId = null,
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_detects_enum_member_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -11 +11,0 @@ public enum OperatorWorkItemKindDto
-    ProviderTrustGate = 6
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_ignores_additive_optional_record_parameter(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -109,0 +110 @@ public sealed record TradingOperatorReadinessDto(
+    IReadOnlyList<string>? NewWarnings = null);
"""

        self.assertFalse(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_ignores_private_local_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Strategies/Services/StrategyRunReadService.cs b/src/Meridian.Strategies/Services/StrategyRunReadService.cs
@@ -44 +44,0 @@ public sealed class StrategyRunReadService
-        var latestRunCount = summaries.Count;
"""

        self.assertFalse(gate.patch_has_breaking_removal(patch))

    def test_patch_has_breaking_removal_detects_continuity_dto_member_removal(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/ReconciliationContinuityDtos.cs b/src/Meridian.Contracts/Workstation/ReconciliationContinuityDtos.cs
@@ -31 +31,0 @@ public sealed record ReconciliationContinuityDto(
-    decimal PendingCashFlow,
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_patch_has_breaking_removal_detects_continuity_dto_member_rename(self) -> None:
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/LedgerContinuityDtos.cs b/src/Meridian.Contracts/Workstation/LedgerContinuityDtos.cs
@@ -18 +18 @@ public sealed record LedgerContinuityDto(
-    decimal SettledCash,
+    decimal AvailableCash,
"""

        self.assertTrue(gate.patch_has_breaking_removal(patch))


    def test_find_missing_security_master_instrument_references_flags_governance_dto_without_identity_fields(self) -> None:
        with governance_dto_fixture(
            "public sealed record GovernanceInstrumentContractDto(string InstrumentTicker);"
        ) as path:
            changed_files = [path]
            patch = f"""
diff --git a/{changed_files[0]} b/{changed_files[0]}
--- a/{changed_files[0]}
+++ b/{changed_files[0]}
@@ -1,0 +1 @@
+    string InstrumentTicker,
"""
            violations = gate.find_missing_security_master_instrument_references(changed_files, patch)
            self.assertEqual(changed_files, violations)


    def test_find_missing_security_master_instrument_references_allows_security_master_linked_dto(self) -> None:
        with governance_dto_fixture(
            "public sealed record GovernanceInstrumentContractDto(string InstrumentTicker, Guid SecurityId, string SecurityMasterSource);"
        ) as path:
            changed_files = [path]
            patch = f"""
diff --git a/{changed_files[0]} b/{changed_files[0]}
--- a/{changed_files[0]}
+++ b/{changed_files[0]}
@@ -1,0 +1,2 @@
+    string InstrumentTicker,
+    Guid SecurityId,
"""
            violations = gate.find_missing_security_master_instrument_references(changed_files, patch)
            self.assertEqual([], violations)

    def test_find_required_field_regressions_flags_required_property_addition(self) -> None:
        changed_files = ["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"]
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
+++ b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -200,0 +201 @@ public sealed class TradingOperatorReadinessDto
+    public required string ContractVersion { get; init; }
"""

        violations = gate.find_required_field_regressions(changed_files, patch)
        self.assertEqual(changed_files, violations)

    def test_find_required_field_regressions_flags_non_nullable_record_parameter_addition(self) -> None:
        changed_files = ["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"]
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
+++ b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -102,0 +103 @@ public sealed record TradingOperatorReadinessDto(
+    string ContractVersion,
"""

        violations = gate.find_required_field_regressions(changed_files, patch)
        self.assertEqual(changed_files, violations)

    def test_find_required_field_regressions_flags_required_primitive_array_property_addition(self) -> None:
        changed_files = ["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"]
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
+++ b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -200,0 +201 @@ public sealed class TradingOperatorReadinessDto
+    public required string[] Symbols { get; init; }
"""

        violations = gate.find_required_field_regressions(changed_files, patch)
        self.assertEqual(changed_files, violations)

    def test_find_required_field_regressions_flags_non_nullable_primitive_array_record_parameter_addition(self) -> None:
        changed_files = ["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"]
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
+++ b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -102,0 +103 @@ public sealed record TradingOperatorReadinessDto(
+    string[] Symbols,
"""

        violations = gate.find_required_field_regressions(changed_files, patch)
        self.assertEqual(changed_files, violations)

    def test_find_required_field_regressions_allows_nullable_record_parameter_addition(self) -> None:
        changed_files = ["src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs"]
        patch = """
diff --git a/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
+++ b/src/Meridian.Contracts/Workstation/TradingOperatorReadinessDtos.cs
@@ -102,0 +103 @@ public sealed record TradingOperatorReadinessDto(
+    string? ContractVersion = null,
"""

        violations = gate.find_required_field_regressions(changed_files, patch)
        self.assertEqual([], violations)


if __name__ == "__main__":
    unittest.main()
