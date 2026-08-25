"""Tests for the C#/TypeScript contract parity gate.

The gate exists to catch a class of drift the compiler cannot: the dashboard casts parsed JSON to
its interface, so a C# record that renames a member, changes its nullability, or turns a scalar
into a collection reaches the browser as a silently wrong shape. These tests pin the behaviours
that make the gate worth having — it fails on each of those three drifts, it fails when a registry
entry names something that no longer exists, and its known-divergence list ratchets down rather
than becoming a permanent allowance.
"""

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-contract-type-parity.py"
SPEC = importlib.util.spec_from_file_location("check_contract_type_parity", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)

REPO_REGISTRY = (
    Path(__file__).resolve().parents[2] / "build" / "config" / "contracts" / "type-parity-registry.json"
)

CSHARP = """
namespace Meridian.Contracts.Workstation;

public sealed record SampleDto(
    string Title,
    bool IsBlocked,
    IReadOnlyList<SampleRowDto> Rows,
    SampleSelectionDto? Selection);

public sealed record OtherDto(
    string Name);
"""

TYPESCRIPT = """
export interface SampleDto {
  title: string;
  isBlocked: boolean;
  rows: SampleRowDto[];
  selection: SampleSelectionDto | null;
}

export interface OtherDto {
  name: string;
}
"""


class ContractTypeParityTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        (self.root / "cs").mkdir()
        (self.root / "ts").mkdir()
        self.csharp_path = self.root / "cs" / "Sample.cs"
        self.typescript_path = self.root / "ts" / "sample.ts"
        self.csharp_path.write_text(CSHARP, encoding="utf-8")
        self.typescript_path.write_text(TYPESCRIPT, encoding="utf-8")
        self.addCleanup(self._temp.cleanup)

    def pair(self, name: str) -> dict:
        return {
            "csharp_file": "cs/Sample.cs",
            "csharp_record": name,
            "typescript_file": "ts/sample.ts",
            "typescript_interface": name,
        }

    def write_registry(self, **overrides) -> Path:
        registry = {"pairs": [self.pair("SampleDto")], "known_divergences": []}
        registry.update(overrides)
        path = self.root / "registry.json"
        path.write_text(json.dumps(registry), encoding="utf-8")
        return path

    def run_gate(self, registry: Path) -> int:
        return MODULE.main(["--registry", str(registry), "--repo-root", str(self.root)])

    def test_matching_declarations_pass(self) -> None:
        self.assertEqual(0, self.run_gate(self.write_registry()))

    def test_renamed_member_fails(self) -> None:
        self.typescript_path.write_text(TYPESCRIPT.replace("title:", "heading:"), encoding="utf-8")
        self.assertEqual(1, self.run_gate(self.write_registry()))

    def test_nullability_drift_fails(self) -> None:
        # C# keeps `SampleSelectionDto?`; TypeScript drops the null union.
        self.typescript_path.write_text(
            TYPESCRIPT.replace("selection: SampleSelectionDto | null;", "selection: SampleSelectionDto;"),
            encoding="utf-8",
        )
        self.assertEqual(1, self.run_gate(self.write_registry()))

    def test_optional_marker_counts_as_nullable(self) -> None:
        # `selection?: T` is as good as `T | null` for a C# `T?`.
        self.typescript_path.write_text(
            TYPESCRIPT.replace("selection: SampleSelectionDto | null;", "selection?: SampleSelectionDto;"),
            encoding="utf-8",
        )
        self.assertEqual(0, self.run_gate(self.write_registry()))

    def test_collection_drift_fails(self) -> None:
        self.typescript_path.write_text(
            TYPESCRIPT.replace("rows: SampleRowDto[];", "rows: SampleRowDto;"), encoding="utf-8"
        )
        self.assertEqual(1, self.run_gate(self.write_registry()))

    def test_missing_record_fails_rather_than_skipping(self) -> None:
        registry = self.write_registry(
            pairs=[
                {
                    "csharp_file": "cs/Sample.cs",
                    "csharp_record": "NoSuchDto",
                    "typescript_file": "ts/sample.ts",
                    "typescript_interface": "SampleDto",
                }
            ]
        )
        self.assertEqual(1, self.run_gate(registry))

    def test_empty_registry_is_rejected_not_vacuously_passed(self) -> None:
        registry = self.write_registry(pairs=[])
        self.assertEqual(2, self.run_gate(registry))

    def test_known_divergence_does_not_fail_while_it_still_diverges(self) -> None:
        # SampleDto is drifted and recorded as such; OtherDto is the enforced pair.
        self.typescript_path.write_text(TYPESCRIPT.replace("title:", "heading:"), encoding="utf-8")
        registry = self.write_registry(
            pairs=[self.pair("OtherDto")],
            known_divergences=[self.pair("SampleDto")],
        )
        self.assertEqual(0, self.run_gate(registry))

    def test_new_drift_still_fails_alongside_a_known_divergence(self) -> None:
        # Both drift: the recorded one is tolerated, the enforced one is not.
        self.typescript_path.write_text(
            TYPESCRIPT.replace("title:", "heading:").replace("name: string;", "label: string;"),
            encoding="utf-8",
        )
        registry = self.write_registry(
            pairs=[self.pair("OtherDto")],
            known_divergences=[self.pair("SampleDto")],
        )
        self.assertEqual(1, self.run_gate(registry))

    def test_repaired_divergence_must_be_promoted(self) -> None:
        # SampleDto agrees, yet is still recorded as diverging: the ratchet demands promotion so
        # the list cannot rot into a permanent allowance.
        registry = self.write_registry(
            pairs=[self.pair("OtherDto")],
            known_divergences=[self.pair("SampleDto")],
        )
        self.assertEqual(1, self.run_gate(registry))

    def test_camel_case_matches_serializer_policy(self) -> None:
        self.assertEqual("explorerId", MODULE.camel_case("ExplorerId"))
        self.assertEqual("isBlocked", MODULE.camel_case("IsBlocked"))
        self.assertEqual("ioPath", MODULE.camel_case("IOPath"))

    def test_repository_registry_is_well_formed(self) -> None:
        registry = json.loads(REPO_REGISTRY.read_text(encoding="utf-8"))
        self.assertTrue(registry["pairs"], "the registry must enforce at least one pair")
        seen: set[tuple[str, str]] = set()
        for group in ("pairs", "known_divergences"):
            for entry in registry.get(group, []):
                for field in ("csharp_file", "csharp_record", "typescript_file", "typescript_interface"):
                    self.assertIn(field, entry)
                    self.assertTrue(str(entry[field]).strip())
                key = (group, entry["typescript_interface"])
                self.assertNotIn(key, seen, f"{entry['typescript_interface']} is listed twice in {group}")
                seen.add(key)

        enforced = {entry["typescript_interface"] for entry in registry["pairs"]}
        exempt = {entry["typescript_interface"] for entry in registry.get("known_divergences", [])}
        self.assertEqual(
            set(),
            enforced & exempt,
            "a pair cannot be both enforced and exempt",
        )


class DocumentedMemberParsingTests(unittest.TestCase):
    """A documented member must be compared, not skipped.

    The parser split the interface body on ";" and dropped any chunk starting with a comment, so a
    member carrying a JSDoc block was invisible to the gate — it passed silently rather than being
    checked, which is the one failure mode a drift gate cannot afford.
    """

    def test_member_with_a_jsdoc_block_is_parsed(self):
        source = """
        export interface Sample {
          plain: string;
          /**
           * Explains the member, across
           * several lines.
           */
          documented?: string | null;
        }
        """

        members = MODULE.parse_typescript_interface(source, "Sample")

        self.assertIsNotNone(members)
        self.assertIn("documented", members)
        self.assertIn("plain", members)
        self.assertTrue(members["documented"]["nullable"])

    def test_member_with_a_leading_line_comment_is_parsed(self):
        source = """
        export interface Sample {
          // explains the member
          documented: string;
        }
        """

        members = MODULE.parse_typescript_interface(source, "Sample")

        self.assertIsNotNone(members)
        self.assertIn("documented", members)

if __name__ == "__main__":
    unittest.main()