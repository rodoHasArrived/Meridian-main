from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_contract_compatibility_gate.py"
SPEC = importlib.util.spec_from_file_location("check_contract_compatibility_gate", SCRIPT_PATH)
assert SPEC and SPEC.loader
gate = importlib.util.module_from_spec(SPEC)
sys.modules["check_contract_compatibility_gate"] = gate
SPEC.loader.exec_module(gate)


class ContractCompatibilityGateTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
