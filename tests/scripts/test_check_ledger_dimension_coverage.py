from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-ledger-dimension-coverage.py"
SPEC = importlib.util.spec_from_file_location("check_ledger_dimension_coverage", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_ledger_dimension_coverage"] = guard
SPEC.loader.exec_module(guard)

CANONICAL = """
namespace Meridian.Ledger;

public sealed record LedgerLineDimensionSet(
    string? FundId = null,
    string? EntityId = null,
    string? CostCenterId = null,
    IReadOnlyDictionary<string, string>? ExternalGlDimensions = null,
    string? ProjectId = null)
{
    public Guid? PositionId { get; init; }
}
"""

COMPLETE_DTO = """
public sealed record LedgerDimensionSetDto(
    string? FundId = null,
    string? EntityId = null,
    string? CostCenterId = null,
    IReadOnlyDictionary<string, string>? ExternalGlDimensions = null,
    string? ProjectId = null)
{
    public Guid? PositionId { get; init; }
}
"""

COMPLETE_CONTAINMENT = """
    internal static string? BuildLineDimensionContainmentJson(LedgerLineDimensionSet? dimensions)
    {
        AddDimension(values, "fundId", dimensions.FundId);
        AddDimension(values, "entityId", dimensions.EntityId);
        AddDimension(values, "costCenterId", dimensions.CostCenterId);
        AddDimension(values, "projectId", dimensions.ProjectId);
        values["externalGlDimensions"] = dimensions.ExternalGlDimensions;
        values["positionId"] = dimensions.PositionId;
    }

    internal static LedgerLineDimensionSet? CanonicalizeLineDimensions(LedgerLineDimensionSet? dimensions)
    {
        return new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            ExternalGlDimensions: externalGlDimensions,
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };
    }
"""

COMPLETE_EXPLORER = """
    private static void AddDimensionFilters(ICollection<Filter> filters, IEnumerable<Dto?> sets)
    {
        AddDimensionFilter(filters, "fund", "Fund", dimensions.Select(value => value.FundId));
        AddDimensionFilter(filters, "entity", "Entity", dimensions.Select(value => value.EntityId));
        AddDimensionFilter(filters, "cost-center", "Cost Center", dimensions.Select(value => value.CostCenterId));
        AddDimensionFilter(filters, "project", "Project", dimensions.Select(value => value.ProjectId));
        AddDimensionFilter(filters, "position", "Position", dimensions.Select(value => value.PositionId));
        var external = dimensions.SelectMany(value => value.ExternalGlDimensions);
    }

    private static IReadOnlyList<Item> BuildDimensionFields(Dto? dimensions)
    {
        AddDimensionField(fields, "Fund", dimensions.FundId);
        AddDimensionField(fields, "Entity", dimensions.EntityId);
        AddDimensionField(fields, "Cost Center", dimensions.CostCenterId);
        AddDimensionField(fields, "Project", dimensions.ProjectId);
        AddDimensionField(fields, "Position", dimensions.PositionId);
        foreach (var pair in dimensions.ExternalGlDimensions) { }
    }
"""


def build_tree(
    root: Path,
    canonical: str = CANONICAL,
    dto: str = COMPLETE_DTO,
    containment: str = COMPLETE_CONTAINMENT,
    explorer: str = COMPLETE_EXPLORER,
) -> Path:
    files = {
        guard.CANONICAL_SOURCE: canonical,
        Path("src/Meridian.Contracts/Ledger/AccountingConfigurationDtos.cs"): dto,
        Path("src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Serialization.cs"): containment,
        Path("src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs"): explorer,
    }
    for relative, content in files.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    return root


class CheckLedgerDimensionCoverageTests(unittest.TestCase):
    def test_complete_surfaces_report_no_gaps(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            self.assertEqual(guard.find_gaps(build_tree(Path(temp_dir))), [])

    def test_canonical_dimensions_reads_positional_and_declared_members(self) -> None:
        self.assertEqual(
            guard.canonical_dimensions(CANONICAL),
            ["FundId", "EntityId", "CostCenterId", "ExternalGlDimensions", "ProjectId", "PositionId"],
        )

    def test_a_surface_missing_a_dimension_is_flagged(self) -> None:
        explorer = COMPLETE_EXPLORER.replace(
            '        AddDimensionFilter(filters, "project", "Project", dimensions.Select(value => value.ProjectId));\n',
            "",
        )
        with tempfile.TemporaryDirectory() as temp_dir:
            gaps = guard.find_gaps(build_tree(Path(temp_dir), explorer=explorer))

        self.assertEqual(len(gaps), 1)
        path, member, missing, _ = gaps[0]
        self.assertEqual(path, "src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs")
        self.assertEqual(member, "AddDimensionFilters")
        self.assertEqual(missing, ["ProjectId"])

    def test_containment_surface_is_checked_against_camel_case_wire_keys(self) -> None:
        """That surface names dimensions as JSON keys, not C# members, so a PascalCase-only match
        would report every dimension missing."""
        containment = COMPLETE_CONTAINMENT.replace(
            '        AddDimension(values, "costCenterId", dimensions.CostCenterId);\n', ""
        )
        with tempfile.TemporaryDirectory() as temp_dir:
            gaps = guard.find_gaps(build_tree(Path(temp_dir), containment=containment))

        flagged = {(member, tuple(missing)) for _, member, missing, _ in gaps}
        self.assertIn(("BuildLineDimensionContainmentJson", ("CostCenterId",)), flagged)

    def test_declaration_is_matched_rather_than_an_earlier_call_site(self) -> None:
        """An earlier version matched the first mention of the member name. Where a file calls a
        helper before declaring it -- as both real files do -- it brace-matched an unrelated block
        and reported every dimension as missing everywhere."""
        explorer = """
    private static IReadOnlyList<Filter> BuildLedgerFilters(Run run)
    {
        var filters = new List<Filter>();
        AddDimensionFilters(filters, dimensions);
        return filters;
    }
""" + COMPLETE_EXPLORER

        with tempfile.TemporaryDirectory() as temp_dir:
            self.assertEqual(guard.find_gaps(build_tree(Path(temp_dir), explorer=explorer)), [])

    def test_a_renamed_surface_member_fails_closed(self) -> None:
        """Coverage must never go unchecked silently: a surface whose member vanished is an error,
        not a skip."""
        with tempfile.TemporaryDirectory() as temp_dir:
            root = build_tree(Path(temp_dir), explorer=COMPLETE_EXPLORER.replace("AddDimensionFilters", "AddScopeFilters"))

            with self.assertRaises(SystemExit) as raised:
                guard.find_gaps(root)

        self.assertIn("AddDimensionFilters", str(raised.exception))

    def test_an_unparsable_canonical_source_fails_rather_than_passing_vacuously(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = build_tree(Path(temp_dir), canonical="public sealed record LedgerLineDimensionSet();")

            with self.assertRaises(SystemExit) as raised:
                guard.find_gaps(root)

        self.assertIn("cannot have parsed it correctly", str(raised.exception))

    def test_repository_surfaces_all_carry_every_declared_dimension(self) -> None:
        """The live invariant (ACCT-CHECKLIST-03)."""
        self.assertEqual(guard.find_gaps(REPO_ROOT), [])

    def test_guard_exits_non_zero_and_names_the_surface(self) -> None:
        explorer = COMPLETE_EXPLORER.replace(
            '        AddDimensionField(fields, "Project", dimensions.ProjectId);\n', ""
        )
        with tempfile.TemporaryDirectory() as temp_dir:
            root = build_tree(Path(temp_dir), explorer=explorer)
            completed = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), "--repo-root", str(root)],
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(completed.returncode, 1)
        self.assertIn("BuildDimensionFields", completed.stderr)
        self.assertIn("ProjectId", completed.stderr)

    def test_guard_exits_zero_on_the_repository(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(SCRIPT_PATH)], capture_output=True, text=True, check=False
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)


if __name__ == "__main__":
    unittest.main()
