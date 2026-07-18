from __future__ import annotations

from pathlib import Path
import tempfile
import textwrap
import unittest

from tools.schema_control.contracts import build_contract_manifest


class ContractManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._temporary_directory.cleanup)
        self.root = Path(self._temporary_directory.name)
        self.contracts = self.root / "src" / "Meridian.Contracts"
        self.contracts.mkdir(parents=True)

    def write_contract(self, relative_path: str, source: str) -> Path:
        path = self.contracts / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(textwrap.dedent(source).lstrip(), encoding="utf-8")
        return path

    def build(self, *contract_sets: dict) -> dict:
        if not contract_sets:
            contract_sets = (
                {
                    "id": "all-contracts",
                    "directories": ["src/Meridian.Contracts"],
                    "schemas": [],
                    "diagram": False,
                },
            )
        return build_contract_manifest(
            self.root,
            {"contract_sets": list(contract_sets)},
        )

    @staticmethod
    def objects_by_id(manifest: dict) -> dict[str, dict]:
        return {item["id"]: item for item in manifest["objects"]}

    @staticmethod
    def members_by_name(contract: dict) -> dict[str, dict]:
        return {member["name"]: member for member in contract["members"]}

    def test_multiline_positional_record_captures_types_attributes_and_references(
        self,
    ) -> None:
        self.write_contract(
            "Accounting/LedgerSummaryDto.cs",
            """
            using System.Text.Json.Serialization;

            namespace Meridian.Contracts.Accounting;

            public sealed record LedgerSummaryDto(
                Guid Id,
                [property: JsonPropertyName("bookName")]
                string? Name,
                IReadOnlyList<LedgerLineDto> Lines,
                LedgerLineDto[]? Adjustments = null);

            public sealed record LedgerLineDto(decimal Amount);
            """,
        )

        manifest = self.build()
        contracts = self.objects_by_id(manifest)
        summary = contracts["Meridian.Contracts.Accounting.LedgerSummaryDto"]
        members = self.members_by_name(summary)

        self.assertEqual(summary["id"], summary["full_name"])
        self.assertEqual(summary["id"], summary["qualified_name"])
        self.assertEqual("dto", summary["classification"])
        self.assertEqual("record", summary["kind"])
        self.assertEqual(
            "src/Meridian.Contracts/Accounting/LedgerSummaryDto.cs",
            summary["source"]["path"],
        )
        self.assertEqual(5, summary["source"]["line"])
        self.assertEqual("string?", members["Name"]["raw_type"])
        self.assertEqual(members["Name"]["raw_type"], members["Name"]["type"])
        self.assertTrue(members["Name"]["nullable"])
        self.assertEqual("bookName", members["Name"]["json_name"])
        self.assertEqual("IReadOnlyList<LedgerLineDto>", members["Lines"]["raw_type"])
        self.assertTrue(members["Lines"]["collection"])
        self.assertEqual("IReadOnlyList", members["Lines"]["collection_kind"])
        self.assertEqual("LedgerLineDto", members["Lines"]["element_type"])
        self.assertEqual("array", members["Adjustments"]["collection_kind"])
        self.assertEqual("LedgerLineDto", members["Adjustments"]["element_type"])
        self.assertTrue(members["Adjustments"]["nullable"])
        self.assertEqual("null", members["Adjustments"]["default"])
        self.assertEqual(
            ["Meridian.Contracts.Accounting.LedgerLineDto"],
            summary["references"],
        )

    def test_public_auto_properties_capture_map_shape_and_json_metadata(self) -> None:
        self.write_contract(
            "Reporting/Envelope.cs",
            """
            using System.Text.Json.Serialization;

            namespace Meridian.Contracts.Reporting
            {
                public sealed class Envelope : BaseEnvelope, IEnvelope
                {
                    [JsonPropertyName("items")]
                    public required IReadOnlyDictionary<string, ChildDto?>? Items { get; init; }

                    [JsonIgnore]
                    public string Internal { get; } = string.Empty;

                    public int Count { get; private set; }
                    public string Describe() => "public record FakeDto();";
                }

                public abstract class BaseEnvelope { }
                public interface IEnvelope { }
                public sealed record ChildDto(Guid Id);
            }
            """,
        )

        contracts = self.objects_by_id(self.build())
        envelope = contracts["Meridian.Contracts.Reporting.Envelope"]
        members = self.members_by_name(envelope)

        self.assertEqual({"Count", "Internal", "Items"}, set(members))
        self.assertEqual("items", members["Items"]["json_name"])
        self.assertTrue(members["Items"]["nullable"])
        self.assertEqual("map", members["Items"]["collection_kind"])
        self.assertEqual("string", members["Items"]["key_type"])
        self.assertEqual("ChildDto?", members["Items"]["element_type"])
        self.assertTrue(members["Internal"]["json_ignored"])
        self.assertEqual(["BaseEnvelope", "IEnvelope"], envelope["base_types"])
        self.assertEqual(
            [
                "Meridian.Contracts.Reporting.BaseEnvelope",
                "Meridian.Contracts.Reporting.ChildDto",
                "Meridian.Contracts.Reporting.IEnvelope",
            ],
            envelope["references"],
        )

    def test_enum_members_preserve_explicit_values(self) -> None:
        self.write_contract(
            "Common/WorkflowState.cs",
            """
            namespace Meridian.Contracts.Common;

            public enum WorkflowState
            {
                Unknown,
                Ready = 10,
                Active = Ready,
                Disabled = -1,
                FirstFlag = 1 << 0,
                SecondFlag = 1 << 1,
            }
            """,
        )

        contract = self.objects_by_id(self.build())[
            "Meridian.Contracts.Common.WorkflowState"
        ]
        members = {member["name"]: member for member in contract["enum_members"]}

        self.assertEqual("enum", contract["classification"])
        self.assertFalse(members["Unknown"]["explicit_value"])
        self.assertIsNone(members["Unknown"]["value"])
        self.assertEqual("10", members["Ready"]["value"])
        self.assertEqual("Ready", members["Active"]["value"])
        self.assertEqual("- 1", members["Disabled"]["value"])
        self.assertEqual("1<<0", members["FirstFlag"]["value"])
        self.assertEqual("1<<1", members["SecondFlag"]["value"])

    def test_nested_types_are_not_properties_and_tuple_properties_are_captured(
        self,
    ) -> None:
        self.write_contract(
            "Common/Outer.cs",
            """
            namespace Meridian.Contracts.Common;

            public sealed class Outer
            {
                public (string Code, int Number) Pair { get; init; }

                public sealed class Inner
                {
                    public string Value { get; init; } = string.Empty;
                }
            }
            """,
        )

        contracts = self.objects_by_id(self.build())
        outer = contracts["Meridian.Contracts.Common.Outer"]
        inner = contracts["Meridian.Contracts.Common.Outer.Inner"]

        self.assertEqual({"Pair"}, set(self.members_by_name(outer)))
        self.assertEqual(
            "(string Code, int Number)",
            self.members_by_name(outer)["Pair"]["raw_type"],
        )
        self.assertEqual({"Value"}, set(self.members_by_name(inner)))

    def test_overlapping_sets_deduplicate_objects_and_union_module_mappings(
        self,
    ) -> None:
        self.write_contract(
            "Accounting/PostingCommand.cs",
            """
            namespace Meridian.Contracts.Accounting;

            public sealed record PostingCommand(Guid PostingId);
            """,
        )
        self.write_contract(
            "ReferenceData/CurrencyDto.cs",
            """
            namespace Meridian.Contracts.ReferenceData;

            public sealed record CurrencyDto(string Code);
            """,
        )
        all_contracts = {
            "id": "all-contracts",
            "directories": ["src/Meridian.Contracts"],
            "schemas": [],
            "diagram": False,
        }
        accounting = {
            "id": "accounting-contracts",
            "directories": ["src/Meridian.Contracts"],
            "namespace_prefixes": ["Meridian.Contracts.Accounting"],
            "schemas": ["accounting", "ledger"],
            "diagram": True,
        }

        manifest = self.build(accounting, all_contracts)
        contracts = self.objects_by_id(manifest)
        posting = contracts["Meridian.Contracts.Accounting.PostingCommand"]
        currency = contracts["Meridian.Contracts.ReferenceData.CurrencyDto"]

        self.assertEqual(2, len(contracts))
        self.assertEqual(
            ["accounting-contracts", "all-contracts"],
            posting["contract_sets"],
        )
        self.assertEqual(["accounting", "ledger"], posting["mapped_schemas"])
        self.assertTrue(posting["diagram"])
        self.assertEqual(["accounting-contracts"], posting["diagram_contract_sets"])
        self.assertEqual(["all-contracts"], currency["contract_sets"])
        self.assertEqual([], currency["mapped_schemas"])
        self.assertFalse(currency["diagram"])
        self.assertFalse(manifest["mapping_policy"]["structural_equivalence_claimed"])
        self.assertEqual("module", manifest["mapping_policy"]["level"])

        contract_sets = {entry["id"]: entry for entry in manifest["contract_sets"]}
        self.assertEqual(
            ["Meridian.Contracts.Accounting.PostingCommand"],
            contract_sets["accounting-contracts"]["object_ids"],
        )
        self.assertEqual(2, len(contract_sets["all-contracts"]["object_ids"]))

    def test_namespace_partitions_and_hashes_are_deterministic(self) -> None:
        self.write_contract(
            "B/BetaDto.cs",
            """
            namespace Meridian.Contracts.B;
            public sealed record BetaDto(string Value);
            """,
        )
        self.write_contract(
            "A/AlphaDto.cs",
            """
            namespace Meridian.Contracts.A;
            public sealed record AlphaDto(B.BetaDto Beta);
            """,
        )
        broad = {
            "id": "all-contracts",
            "directories": ["src/Meridian.Contracts"],
            "schemas": [],
            "diagram": False,
        }
        narrow = {
            "id": "a-contracts",
            "directories": ["src/Meridian.Contracts/A"],
            "schemas": ["a_schema"],
            "diagram": True,
        }

        first = self.build(broad, narrow)
        second = self.build(narrow, broad)

        self.assertEqual(first, second)
        self.assertEqual(64, len(first["fingerprint"]))
        self.assertTrue(
            all(len(item["fingerprint"]) == 64 for item in first["objects"])
        )
        self.assertEqual(
            ["Meridian.Contracts.A", "Meridian.Contracts.B"],
            [partition["namespace"] for partition in first["namespace_partitions"]],
        )
        self.assertTrue(
            all(
                len(partition["fingerprint"]) == 64
                for partition in first["namespace_partitions"]
            )
        )

    def test_comments_and_strings_do_not_create_fake_contracts(self) -> None:
        self.write_contract(
            "Safe/RealDto.cs",
            r"""
            namespace Meridian.Contracts.Safe;

            // public sealed record CommentDto(string Value);
            /* public sealed class BlockCommentDto { } */
            public sealed class RealDto
            {
                public string Text { get; init; } = "public record StringDto(int Id);";
            }
            """,
        )

        manifest = self.build()

        self.assertEqual(
            ["Meridian.Contracts.Safe.RealDto"],
            [item["id"] for item in manifest["objects"]],
        )

    def test_empty_string_literal_does_not_hide_following_types(self) -> None:
        self.write_contract(
            "Safe/EmptyStringDto.cs",
            """
            namespace Meridian.Contracts.Safe;

            public sealed class EmptyStringDto
            {
                public string Value { get; init; } = "";
            }

            public sealed record FollowingDto(int Id);
            """,
        )

        manifest = self.build()

        self.assertEqual(
            [
                "Meridian.Contracts.Safe.EmptyStringDto",
                "Meridian.Contracts.Safe.FollowingDto",
            ],
            [item["id"] for item in manifest["objects"]],
        )

    def test_partial_declarations_merge_sources_and_members(self) -> None:
        self.write_contract(
            "Partial/PartialDto.A.cs",
            """
            namespace Meridian.Contracts.Partial;
            public sealed partial class PartialDto
            {
                public string Alpha { get; init; } = string.Empty;
            }
            """,
        )
        self.write_contract(
            "Partial/PartialDto.B.cs",
            """
            namespace Meridian.Contracts.Partial;
            public sealed partial class PartialDto
            {
                public int Beta { get; init; }
            }
            """,
        )

        contract = self.objects_by_id(self.build())[
            "Meridian.Contracts.Partial.PartialDto"
        ]

        self.assertTrue(contract["partial"])
        self.assertEqual({"Alpha", "Beta"}, set(self.members_by_name(contract)))
        self.assertEqual(2, len(contract["sources"]))
        self.assertEqual(
            [
                "src/Meridian.Contracts/Partial/PartialDto.A.cs",
                "src/Meridian.Contracts/Partial/PartialDto.B.cs",
            ],
            [source["path"] for source in contract["sources"]],
        )

    def test_configuration_validation_rejects_duplicate_ids_and_missing_directories(
        self,
    ) -> None:
        duplicate = {
            "id": "duplicate",
            "directories": ["src/Meridian.Contracts"],
        }
        with self.assertRaisesRegex(ValueError, "duplicate contract set id"):
            self.build(duplicate, duplicate)

        with self.assertRaisesRegex(ValueError, "lowercase slug"):
            self.build(
                {
                    "id": "../escaped",
                    "directories": ["src/Meridian.Contracts"],
                }
            )

        with self.assertRaises(FileNotFoundError):
            self.build(
                {
                    "id": "missing",
                    "directories": ["src/Does.Not.Exist"],
                }
            )


if __name__ == "__main__":
    unittest.main()
