"""Regression tests for source-backed adapter inventory validation."""

from __future__ import annotations

import copy
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "build/scripts/docs"))
import adapter_readiness as readiness


def row(folder: str, provider_id: str | None, types: dict | None = None) -> dict:
    slots = dict.fromkeys(readiness.TYPE_SLOTS)
    slots.update(types or {})
    return {
        "folder": folder,
        "provider_id": provider_id,
        "aliases": ["old-sample"] if provider_id == "sample" else [],
        "state": "partial",
        "capabilities": {key: slots[key] is not None for key in readiness.CAPABILITIES},
        "adapter_types": {key: slots[key] for key in readiness.CAPABILITIES},
        "other_types": {key: slots[key] for key in readiness.OTHER_TYPES},
        "requirements": {"credentials": "SAMPLE_API_KEY", "optional_sdk": "None."},
        "risks": ["External endpoint may throttle requests."],
        "degradation": "Returns unavailable when credentials are missing.",
        "registration": [{"path": readiness.CATALOG_PATH, "symbol": "ProviderCapabilityDescriptorCatalog"}],
        "evidence": [{"path": "tests/SampleTests.cs", "symbol": "RejectsMissingCredentials", "kind": "test"}],
        "owner": "core-team",
        "next_action": "Validate with live credentials.",
    }


class AdapterReadinessTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write(readiness.CATALOG_PATH, '''
public static class ProviderCapabilityDescriptorCatalog {
  public static object Descriptors { get; } = [
    // new("commented-out-provider", Streaming: typeof(ImaginaryClient)),
    new("sample", typeof(SampleClient), Search: typeof(SampleClient),
      Exclusions: [new(nameof(IOther), "new(fake), ), ] and commas are opaque")],
      StreamingFactory: static f => f.Create<SampleClient, Dictionary<string, int>>("value,)", (1, 2)))
  ];
  public static object ExcludedAdapterFamilies { get; } = [new("Core", "Shared primitives.")];
}
public sealed record ProviderCapabilityDescriptor(
  string ProviderId, Type? Streaming = null, Type? Historical = null, Type? Search = null,
  Type? CorporateActions = null, Type? Options = null, Type? Brokerage = null,
  Type? SymbolResolver = null, Type? CompatibilityDataSource = null,
  object? Exclusions = null, Func<Factory, object>? StreamingFactory = null);
''')
        self.write(readiness.IDENTITY_PATH, '''
public static class ProviderIdentity {
  public static object CanonicalFamilyIds { get; } = new[] { "sample" }.ToFrozenSet();
  public static object Aliases { get; } = new Dictionary<string, string>() {
    ["old-sample"] = "sample"
  }.ToFrozenDictionary();
}
''')
        self.write(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", "public abstract class BaseClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public sealed class SampleClient : BaseClient, ISymbolSearchProvider {}")
        self.write("tests/SampleTests.cs", '''
public class SampleTests {
  // public void DeletedTest() { }
  private const string Fake = "public void StringOnlyTest() {}";
  private const string Raw = """ public class RawStringOnlyTest {} """;
  [Fact] public async Task RejectsMissingCredentials() { await Task.CompletedTask; }
}
''')
        self.data = {
            "schema": {"id": "meridian.adapter-readiness", "version": "1.0.0"},
            "adapters": [row("Core", None), row("Sample", "sample", {"streaming": "SampleClient", "symbol_search": "SampleClient"})],
        }

    def write(self, relative: str, content: str):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def replace_source(self, relative: str, old: str, new: str):
        path = self.root / relative
        original = path.read_text(encoding="utf-8")
        self.assertIn(old, original)
        path.write_text(original.replace(old, new), encoding="utf-8")

    def assert_rejected(self, fragment: str, data=None):
        errors = readiness.validate_registry(self.root, self.data if data is None else data)
        self.assertTrue(any(fragment in error for error in errors), errors)

    def test_current_repository_registry_matches_current_source(self):
        self.assertEqual([], readiness.validate_registry(ROOT, readiness.load_registry(ROOT)))

    def test_balanced_catalog_with_comments_nested_generics_and_factory_lambdas(self):
        catalog, exclusions = readiness.read_catalog(self.root)
        self.assertEqual({"sample"}, set(catalog))
        self.assertEqual({"Core"}, exclusions)
        self.assertEqual("SampleClient", catalog["sample"]["symbol_search"])
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_unknown_provider_id_and_alias_are_rejected(self):
        self.data["adapters"][1]["provider_id"] = "old-sample"
        self.assert_rejected("unknown or incorrect provider ID")
        self.data["adapters"][1]["provider_id"] = "sample"
        self.data["adapters"][1]["aliases"].append("made-up")
        self.assert_rejected("aliases must match ProviderIdentity")

    def test_missing_alias_is_rejected_when_source_adds_alias(self):
        self.replace_source(readiness.IDENTITY_PATH, '["old-sample"] = "sample"', '["old-sample"] = "sample", ["new-sample"] = "sample"')
        self.assert_rejected("aliases must match ProviderIdentity")

    def test_missing_duplicate_and_unknown_registry_folders_are_rejected(self):
        original = copy.deepcopy(self.data)
        self.data["adapters"].pop()
        self.assert_rejected("registry missing adapter folders: Sample")
        self.data = copy.deepcopy(original)
        self.data["adapters"].append(copy.deepcopy(self.data["adapters"][1]))
        self.assert_rejected("duplicate adapter folder")
        self.data = original
        self.data["adapters"][1]["folder"] = "RemovedVendor"
        self.assert_rejected("unknown or missing adapter folder")

    def test_unregistered_source_folder_is_rejected(self):
        (self.root / readiness.ADAPTER_ROOT / "NewVendor").mkdir()
        self.assert_rejected("source catalog folder coverage mismatch")
        self.assert_rejected("registry missing adapter folders: NewVendor")

    def test_invalid_states_and_missing_required_narrative_are_rejected(self):
        self.data["adapters"][1]["state"] = "production-ready"
        self.data["adapters"][1]["next_action"] = " "
        self.data["adapters"][1]["requirements"]["optional_sdk"] = None
        self.assert_rejected("invalid readiness state")
        self.assert_rejected("next_action must be non-empty text")
        self.assert_rejected("non-empty credentials and optional_sdk")

    def test_capability_flags_are_strict_booleans_matching_catalog(self):
        self.data["adapters"][1]["capabilities"]["historical"] = True
        self.assert_rejected("capabilities.historical: expected False")
        self.data["adapters"][1]["capabilities"]["streaming"] = 1
        self.assert_rejected("capabilities.streaming: expected True")

    def test_type_claims_and_auxiliary_slots_must_match_catalog(self):
        self.data["adapters"][1]["adapter_types"]["streaming"] = "MissingClient"
        self.data["adapters"][1]["other_types"]["symbol_resolver"] = "UnknownResolver"
        self.assert_rejected("adapter_types.streaming: expected 'SampleClient'")
        self.assert_rejected("other_types.symbol_resolver: expected None")

    def test_deleted_adapter_class_is_rejected_despite_matching_registry(self):
        self.replace_source(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "class SampleClient", "class RenamedClient")
        self.assert_rejected("unknown concrete adapter type SampleClient")

    def test_comments_and_strings_cannot_supply_missing_adapter_declarations(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '// public class SampleClient : IMarketDataClient {}\nconst string Fake = "public class SampleClient : IMarketDataClient {}";')
        self.assert_rejected("unknown concrete adapter type SampleClient")

    def test_inherited_contract_removal_is_rejected(self):
        self.replace_source(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", " : IMarketDataClient", "")
        self.assert_rejected("SampleClient does not implement IMarketDataClient")

    def test_generic_arguments_and_constraints_are_not_implemented_contracts(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public sealed class SampleClient<T> : System.Collections.Generic.List<IMarketDataClient>, ISymbolSearchProvider where T : IOptionsChainProvider {}")
        self.assert_rejected("SampleClient does not implement IMarketDataClient")
        types = readiness.read_adapter_types(self.root)
        self.assertFalse(readiness.implements(types, "SampleClient", "IOptionsChainProvider"))

    def test_generic_qualified_base_can_inherit_real_contract(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", "public abstract class BaseClient<T> : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public sealed class SampleClient : Example.BaseClient<string>, ISymbolSearchProvider {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_new_known_adapter_capability_requires_catalog_update(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", "public sealed class NewOptions : IOptionsChainProvider {}")
        self.assert_rejected("missing known adapter capability implemented by NewOptions")

    def test_removing_catalog_capability_and_registry_claim_still_rejects_source_drift(self):
        self.replace_source(readiness.CATALOG_PATH, ", Search: typeof(SampleClient)", "")
        self.data["adapters"][1]["capabilities"]["symbol_search"] = False
        self.data["adapters"][1]["adapter_types"]["symbol_search"] = None
        self.assert_rejected("missing known adapter capability implemented by SampleClient")

    def test_missing_catalog_family_is_rejected(self):
        self.replace_source(readiness.CATALOG_PATH, 'new("sample", typeof(SampleClient)', 'new("other", typeof(SampleClient)')
        self.assert_rejected("catalog has unknown provider ID: other")
        self.assert_rejected("canonical provider identities do not match")

    def test_unsupported_catalog_syntax_and_duplicate_ids_fail_closed(self):
        self.replace_source(readiness.CATALOG_PATH, 'new("sample", typeof(SampleClient)', 'CreateDescriptor("sample", typeof(SampleClient)')
        self.assert_rejected("unsupported catalog row")

    def test_duplicate_catalog_provider_is_rejected(self):
        self.replace_source(readiness.CATALOG_PATH, 'new("sample", typeof(SampleClient),', 'new("sample", typeof(SampleClient)), new("sample", typeof(SampleClient),')
        self.assert_rejected("duplicate catalog provider ID")

    def test_unknown_named_catalog_argument_fails_closed(self):
        self.replace_source(readiness.CATALOG_PATH, "Search: typeof", "NewCapability: typeof")
        self.assert_rejected("unknown or duplicate descriptor argument")

    def test_missing_test_file_and_symbol_are_rejected(self):
        evidence = self.data["adapters"][1]["evidence"][0]
        evidence["path"] = "tests/Missing.cs"
        self.assert_rejected("referenced file is missing")
        evidence["path"] = "tests/SampleTests.cs"
        evidence["symbol"] = "RenamedTest"
        self.assert_rejected("stale reference symbol RenamedTest")

    def test_comments_strings_and_invocations_cannot_supply_evidence_symbols(self):
        evidence = self.data["adapters"][1]["evidence"][0]
        for symbol in ("DeletedTest", "StringOnlyTest", "RawStringOnlyTest", "CompletedTask"):
            with self.subTest(symbol=symbol):
                evidence["symbol"] = symbol
                self.assert_rejected(f"stale reference symbol {symbol}")

    def test_missing_or_mislabeled_test_evidence_is_rejected(self):
        evidence = self.data["adapters"][1]["evidence"][0]
        evidence["kind"] = "source"
        self.assert_rejected("evidence must include a targeted test reference")
        evidence["kind"] = "test"
        evidence["path"] = readiness.CATALOG_PATH
        evidence["symbol"] = "ProviderCapabilityDescriptorCatalog"
        self.assert_rejected("test evidence must be under tests/")

    def test_absolute_traversal_and_windows_paths_are_rejected(self):
        evidence = self.data["adapters"][1]["evidence"][0]
        for path in ("../tests/SampleTests.cs", "/tmp/SampleTests.cs", "C:/tests/SampleTests.cs", "tests\\SampleTests.cs", "tests/../tests/SampleTests.cs"):
            with self.subTest(path=path):
                evidence["path"] = path
                self.assert_rejected("invalid repository-relative path")

    def test_resolved_symlink_target_cannot_escape_repository(self):
        # Simulate link resolution so this security check also runs on Windows
        # hosts without the privilege required to create filesystem symlinks.
        outside = self.root.parent / "outside/SampleTests.cs"
        with patch.object(Path, "resolve", side_effect=[outside, self.root]):
            with self.assertRaisesRegex(readiness.SourceError, "path escapes repository root"):
                readiness._safe_path(self.root, "tests/SampleTests.cs")

    def test_malformed_state_reference_and_boolean_types_report_errors(self):
        self.data["adapters"][1]["state"] = []
        self.data["adapters"][1]["capabilities"]["streaming"] = "true"
        self.data["adapters"][1]["evidence"][0]["kind"] = {"bad": "kind"}
        self.data["adapters"][1]["registration"] = "not a list"
        self.assert_rejected("invalid readiness state")
        self.assert_rejected("capabilities.streaming")
        self.assert_rejected("invalid evidence kind")
        self.assert_rejected("registration must be a non-empty reference list")

    def test_malformed_registry_and_duplicate_yaml_keys_fail_closed(self):
        self.assert_rejected("expected schema", data="not a mapping")
        self.write(readiness.REGISTRY_PATH, "schema: {}\nschema: {}\n")
        with self.assertRaisesRegex(readiness.SourceError, "duplicate YAML key"):
            readiness.load_registry(self.root)

    def test_cli_failure_is_nonzero_and_success_is_zero(self):
        self.write(readiness.REGISTRY_PATH, readiness.yaml.safe_dump(self.data, sort_keys=False))
        command = [sys.executable, str(ROOT / "build/scripts/docs/validate-adapter-readiness.py"), "--root", str(self.root), "--summary"]
        result = subprocess.run(command, capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.data["adapters"][1]["state"] = "invalid"
        self.write(readiness.REGISTRY_PATH, readiness.yaml.safe_dump(self.data, sort_keys=False))
        result = subprocess.run(command, capture_output=True, text=True, check=False)
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("invalid readiness state", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
