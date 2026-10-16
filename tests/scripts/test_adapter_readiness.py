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
        "registration": [{"path": readiness.CATALOG_PATH, "symbol": "ProviderCapabilityDescriptorCatalog"}] if provider_id else [],
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

    def test_source_alias_duplicates_are_rejected_case_insensitively(self):
        original = (self.root / readiness.IDENTITY_PATH).read_text(encoding="utf-8")
        for alias, canonical in (("old-sample", "sample"), ("OLD-SAMPLE", "sample"), ("Old-Sample", "other")):
            with self.subTest(alias=alias, canonical=canonical):
                self.write(readiness.IDENTITY_PATH, original.replace(
                    '["old-sample"] = "sample"', f'["old-sample"] = "sample", ["{alias}"] = "{canonical}"'))
                self.assert_rejected(f"duplicate source alias {alias}")

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
        self.write(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", "namespace Example; public abstract class BaseClient<T> : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public sealed class SampleClient : Example.BaseClient<string>, ISymbolSearchProvider {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_new_known_adapter_capability_requires_catalog_update(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", "public sealed class NewOptions : IOptionsChainProvider {}")
        self.assert_rejected("missing known adapter capability implemented by NewOptions")

    def test_same_simple_name_in_different_namespaces_cannot_union_capabilities(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public class SampleClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace Two; public class SampleClient : ISymbolSearchProvider {}")
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_partial_declarations_must_share_qualified_identity(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public partial class SampleClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace Two; public partial class SampleClient : ISymbolSearchProvider {}")
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_same_qualified_partial_type_can_union_its_interfaces(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public sealed partial class SampleClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One { public sealed partial class SampleClient : ISymbolSearchProvider {} }")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_partial_fragment_can_omit_explicit_public_modifier(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public sealed partial class SampleClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One; partial class SampleClient : ISymbolSearchProvider {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_abstract_partial_modifier_applies_to_the_whole_type(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public partial class SampleClient : IMarketDataClient, ISymbolSearchProvider {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One; abstract partial class SampleClient {}")
        self.assert_rejected("unknown concrete adapter type SampleClient")

    def test_abstract_partial_helpers_do_not_add_runtime_capabilities(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Helper.cs", "public abstract partial class Helper : IOptionsChainProvider {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Helper.Other.cs", "public partial class Helper {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_partial_records_accept_equivalent_record_class_notation(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public partial record SampleClient : IMarketDataClient;")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One; partial record class SampleClient : ISymbolSearchProvider;")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_partial_types_with_different_generic_arities_cannot_merge(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public partial class SampleClient<T> : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One; public partial class SampleClient<T, U> : ISymbolSearchProvider {}")
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_same_qualified_nonpartial_duplicates_are_rejected(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "namespace One; public class SampleClient : IMarketDataClient {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Other.cs", "namespace One; public class SampleClient : ISymbolSearchProvider {}")
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_same_file_duplicates_are_not_assumed_to_be_conditional(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
namespace One;
public class SampleClient : IMarketDataClient {}
public class SampleClient : ISymbolSearchProvider {}
''')
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_comments_cannot_forge_mutually_exclusive_declarations(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
namespace One;
/*
#if SDK
*/
public class SampleClient : IMarketDataClient {}
/*
#else
*/
public class SampleClient : ISymbolSearchProvider {}
''')
        self.assert_rejected("ambiguous source adapter type SampleClient")

    def test_same_file_mutually_exclusive_sdk_declarations_remain_supported(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
#if SDK
namespace One;
public class SampleClient : IMarketDataClient, ISymbolSearchProvider {}
#else
namespace One;
public class SampleClient : IMarketDataClient, ISymbolSearchProvider {}
#endif
''')
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_conditional_variants_cannot_hide_a_concrete_runtime_adapter(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", '''
#if SDK
public abstract class NewOptions : IOptionsChainProvider {}
#else
public class NewOptions : IOptionsChainProvider {}
#endif
''')
        self.assert_rejected("incompatible conditional adapter declarations NewOptions")

    def test_nested_public_helpers_are_not_runtime_adapter_candidates(self):
        for container in ("internal class", "public class", "public record", "public struct", "public interface"):
            with self.subTest(container=container):
                self.write(f"{readiness.ADAPTER_ROOT}/Sample/Helpers.cs", f"{container} Container {{ public class NestedOptions : IOptionsChainProvider {{}} }}")
                self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_catalog_cannot_reference_nested_public_implementation(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public class Container { public class SampleClient : IMarketDataClient, ISymbolSearchProvider {} }")
        self.assert_rejected("unknown concrete adapter type SampleClient")

    def test_scoped_aliases_resolve_capability_interfaces(self):
        self.write("src/Meridian.ProviderSdk/IMarketDataClient.cs", "namespace Meridian.Infrastructure; public interface IMarketDataClient {}")
        for declaration in (
            "using Stream = global::Meridian.Infrastructure.IMarketDataClient; namespace One; public class SampleClient : Stream, ISymbolSearchProvider {}",
            "namespace One { using Infra = Meridian.Infrastructure; public class SampleClient : Infra.IMarketDataClient, ISymbolSearchProvider {} }",
            "using Infra = global::Meridian.Infrastructure; namespace One; public class SampleClient : Infra::IMarketDataClient, ISymbolSearchProvider {}",
        ):
            with self.subTest(declaration=declaration):
                self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", declaration)
                self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_aliased_capability_missing_from_catalog_is_rejected(self):
        self.write("src/Meridian.ProviderSdk/IOptionsChainProvider.cs", "namespace Contracts; public interface IOptionsChainProvider {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", "using Options = Contracts.IOptionsChainProvider; public class NewOptions : Options {}")
        self.assert_rejected("missing known adapter capability implemented by NewOptions")

    def test_aliases_do_not_leak_between_sibling_namespace_scopes(self):
        self.write("src/Meridian.ProviderSdk/IMarketDataClient.cs", "namespace Contracts; public interface IMarketDataClient {}")
        self.write("src/Meridian.ProviderSdk/IOptionsChainProvider.cs", "namespace Contracts; public interface IOptionsChainProvider {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
namespace One { using Contract = Contracts.IOptionsChainProvider; public abstract class Helper : Contract {} }
namespace Two { using Contract = Contracts.IMarketDataClient; public class SampleClient : Contract, ISymbolSearchProvider {} }
''')
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_unsupported_alias_target_in_base_list_fails_closed(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "using Stream = (int Left, int Right); public class SampleClient : Stream, ISymbolSearchProvider {}")
        self.assert_rejected("unsupported base alias")

    def test_unrelated_qualified_contract_suffix_is_not_a_capability(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public class SampleClient : Unrelated.IMarketDataClient, ISymbolSearchProvider {}")
        self.assert_rejected("SampleClient does not implement IMarketDataClient")

    def test_alias_target_must_resolve_exact_qualified_type(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "using Stream = Unrelated.IMarketDataClient; public class SampleClient : Stream, ISymbolSearchProvider {}")
        self.assert_rejected("unresolved base alias Stream")

    def test_unused_unsupported_aliases_do_not_affect_adapter_detection(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "using Pair = (int Left, int Right); using Number = int; public class SampleClient : IMarketDataClient, ISymbolSearchProvider {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_conflicting_conditional_aliases_cannot_hide_a_capability(self):
        self.write("src/Meridian.ProviderSdk/Contracts.cs", "namespace Contracts; public interface IOptionsChainProvider {} public interface Marker {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", '''
#if SDK
using Optional = Contracts.IOptionsChainProvider;
#else
using Optional = Contracts.Marker;
#endif
public class NewOptions : Optional {}
''')
        self.assert_rejected("conflicting scoped base alias Optional")

    def test_namespace_alias_can_shadow_outer_alias(self):
        self.write("src/Meridian.ProviderSdk/Contracts.cs", "namespace Contracts; public interface IMarketDataClient {} public interface IOptionsChainProvider {}")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
using Contract = Contracts.IOptionsChainProvider;
namespace One {
    using Contract = Contracts.IMarketDataClient;
    public class SampleClient : Contract, ISymbolSearchProvider {}
}
''')
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_record_capabilities_missing_from_catalog_are_rejected(self):
        for declaration in (
            "public sealed record NewOptions : IOptionsChainProvider {}",
            "public sealed record class NewOptions : IOptionsChainProvider {}",
            "public sealed record NewOptions(string ApiKey) : IOptionsChainProvider;",
            "public sealed record class NewOptions(string ApiKey) : IOptionsChainProvider;",
        ):
            with self.subTest(declaration=declaration):
                self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", declaration)
                self.assert_rejected("missing known adapter capability implemented by NewOptions")

    def test_catalog_can_reference_public_record_implementations(self):
        for declaration in (
            "public sealed record SampleClient : IMarketDataClient, ISymbolSearchProvider {}",
            "public sealed record class SampleClient : IMarketDataClient, ISymbolSearchProvider {}",
            "public sealed record SampleClient(string ApiKey) : IMarketDataClient, ISymbolSearchProvider;",
            "public sealed record class SampleClient(string ApiKey) : IMarketDataClient, ISymbolSearchProvider;",
        ):
            with self.subTest(declaration=declaration):
                self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", declaration)
                self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_record_inheritance_handles_positional_parameter_attributes(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", "public abstract record BaseClient(string ApiKey) : IMarketDataClient;")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", '''
public sealed record class SampleClient([property: JsonPropertyName("key")] string ApiKey)
    : BaseClient(ApiKey), ISymbolSearchProvider;
''')
        self.assertEqual([], readiness.validate_registry(self.root, self.data))
        self.replace_source(f"{readiness.ADAPTER_ROOT}/Core/BaseClient.cs", " : IMarketDataClient", "")
        self.assert_rejected("SampleClient does not implement IMarketDataClient")

    def test_record_structs_are_not_class_adapter_implementations(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", "public readonly record struct NewOptions(string Key) : IOptionsChainProvider;")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/SampleClient.cs", "public record struct SampleClient : IMarketDataClient, ISymbolSearchProvider {}")
        self.assert_rejected("unknown concrete adapter type SampleClient")

    def test_nonpublic_and_abstract_records_do_not_add_runtime_capabilities(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/NewOptions.cs", '''
internal sealed record NewOptions : IOptionsChainProvider;
public abstract record class AbstractOptions : IOptionsChainProvider;
''')
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_unrelated_data_records_do_not_create_ambiguous_adapter_types(self):
        self.write(f"{readiness.ADAPTER_ROOT}/Core/Value.cs", "public sealed record Value(string Key);")
        self.write(f"{readiness.ADAPTER_ROOT}/Sample/Value.cs", "public sealed record class Value(string Key) {}")
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

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

    def test_registration_references_must_target_source_tree(self):
        self.data["adapters"][1]["registration"] = [{"path": "tests/SampleTests.cs", "symbol": "SampleTests"}]
        self.assert_rejected("registration references must be under src/")
        self.write("tools/Registration.cs", "public class Registration {}")
        self.data["adapters"][1]["registration"] = [{"path": "tools/Registration.cs", "symbol": "Registration"}]
        self.assert_rejected("registration references must be under src/")

    def test_source_registration_and_separate_test_evidence_remain_valid(self):
        self.write("src/Registration.cs", "public class Registration {}")
        self.data["adapters"][1]["registration"] = [{"path": "src/Registration.cs", "symbol": "Registration"}]
        self.data["adapters"][1]["evidence"].append({"path": "tests/SampleTests.cs", "symbol": "SampleTests", "kind": "source"})
        self.assertEqual([], readiness.validate_registry(self.root, self.data))

    def test_excluded_family_allows_empty_registration_but_still_requires_list(self):
        self.data["adapters"][0]["registration"] = []
        self.assertEqual([], readiness.validate_registry(self.root, self.data))
        self.data["adapters"][0]["registration"] = None
        self.assert_rejected("registration must be a reference list")

    def test_catalogued_family_requires_nonempty_registration(self):
        self.data["adapters"][1]["registration"] = []
        self.assert_rejected("registration must be a non-empty reference list")

    def test_exclusion_catalog_is_not_a_runtime_registration(self):
        for path in (readiness.CATALOG_PATH, readiness.CATALOG_PATH.replace("/Core/", "/Core//")):
            with self.subTest(path=path):
                self.data["adapters"][0]["registration"] = [{"path": path, "symbol": "ProviderCapabilityDescriptorCatalog"}]
                self.assert_rejected("catalog exclusion is not a runtime registration")

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
