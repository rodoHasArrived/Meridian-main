import importlib.util
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-dashboard-type-barrel.py"
SPEC = importlib.util.spec_from_file_location("check_dashboard_type_barrel", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class BarrelFixture:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.types_dir = root / "types"
        self.types_dir.mkdir(parents=True)
        self.barrel = root / "types.ts"

        self.write_module("workstation-1", ["export interface LedgerRowDto { id: string; }"])
        self.write_module("workstation-2", ["export type PortfolioTone = 'Info' | 'Warning';"])
        self.write_barrel(["workstation-1", "workstation-2"])

    def write_module(self, name: str, lines: list[str]) -> None:
        (self.types_dir / f"{name}.ts").write_text("\n".join(lines) + "\n", encoding="utf-8")

    def write_barrel(self, modules: list[str]) -> None:
        body = "\n".join(f'export * from "./types/{module}";' for module in modules)
        self.barrel.write_text(body + "\n", encoding="utf-8")

    def evaluate(self):
        return MODULE.evaluate(self.barrel, self.types_dir)


class DashboardTypeBarrelTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_clean_barrel_has_no_problems(self):
        problems, counts = self.fixture.evaluate()

        self.assertEqual(problems, [])
        self.assertEqual(counts["modules"], 2)
        self.assertEqual(counts["exported_names"], 2)
        self.assertEqual(counts["duplicates"], 0)

    def test_duplicate_declaration_across_modules_fails(self):
        self.fixture.write_module("workstation-2", ["export interface LedgerRowDto { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("LedgerRowDto" in p and "2 barrel modules" in p for p in problems), msg=problems)

    def test_barrel_pointing_at_a_missing_module_fails(self):
        self.fixture.write_barrel(["workstation-1", "workstation-9"])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(any("workstation-9" in p and "does not exist" in p for p in problems), msg=problems)

    def test_contract_module_outside_the_barrel_fails(self):
        self.fixture.write_module("workstation-3", ["export type Orphan = string;"])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(any("workstation-3.ts is neither re-exported" in p for p in problems), msg=problems)

    def test_declared_standalone_module_is_allowed(self):
        standalone = sorted(MODULE.STANDALONE_MODULES)[0]
        self.fixture.write_module(standalone, ["export type Local = string;"])

        problems, _ = self.fixture.evaluate()

        self.assertEqual(problems, [])

    def test_test_modules_are_not_treated_as_contracts(self):
        (self.fixture.types_dir / "workstation-1.test.ts").write_text(
            "export const fixture = 1;\n", encoding="utf-8"
        )

        problems, _ = self.fixture.evaluate()

        self.assertEqual(problems, [])

    def test_detects_a_duplicate_among_indented_module_scope_declarations(self):
        # workstation-3.ts carries 11 module-scope declarations indented by a removed wrapper.
        # Indentation is not nesting, so requiring column zero hid all 11 and any duplicate.
        self.fixture.write_module(
            "workstation-1",
            ["export interface Anchor { id: string; }", "", "  export interface StrayDto {", "    id: string;", "  }"],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export interface StrayDto { id: string; }"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("StrayDto" in p for p in problems), msg=problems)

    def test_ignores_declarations_nested_inside_a_module_or_namespace_block(self):
        # `export *` re-exports module-scope names only. A name inside `declare module 'x'` or
        # `export namespace N` is reachable as N.Row at most, so treating it as a barrel export
        # would block CI over a collision TypeScript never sees.
        self.fixture.write_module(
            "workstation-1",
            ["declare module 'external' {", "  export interface NestedDto { id: string; }", "}"],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export namespace Reporting {", "  export interface NestedDto { id: string; }", "}"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)
        self.assertEqual(problems, [])

    def test_ignores_a_declaration_inside_a_block_comment(self):
        # A phantom name here collides with the real declaration elsewhere and blocks a valid
        # change over a duplicate TypeScript never emitted.
        self.fixture.write_module(
            "workstation-2",
            ["/*", " * export interface LedgerRowDto { id: string; }", " */", "export type Tone = 'Info';"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0)
        self.assertEqual(problems, [])

    def test_detects_a_duplicate_published_by_a_named_re_export(self):
        # `export { X } from "..."` publishes X exactly as a declaration does, so it collides
        # under the barrel's `export *` the same way. Matching only declarations meant the
        # ambiguous name vanished from '@/types' while the gate reported zero duplicates.
        self.fixture.write_module(
            "workstation-1",
            ['export { LedgerRowDto } from "../contracts";'],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export interface LedgerRowDto { id: string; }"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("LedgerRowDto" in p for p in problems), msg=problems)

    def test_counts_the_alias_of_a_renamed_re_export(self):
        self.fixture.write_module(
            "workstation-1",
            ['export { InternalRow as LedgerRowDto } from "../contracts";'],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export interface LedgerRowDto { id: string; }"],
        )

        problems, counts = self.fixture.evaluate()

        # The alias is what '@/types' publishes, so the alias is what can collide.
        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("LedgerRowDto" in p for p in problems), msg=problems)

    def test_a_regex_literal_brace_does_not_hide_later_declarations(self):
        # declared_names infers nesting from brace depth. An unmatched brace inside a regex
        # literal would raise the depth for the rest of the file, dropping every later export
        # so the gate reported zero duplicates no matter what was duplicated.
        self.fixture.write_module(
            "workstation-1",
            [
                "export const BRACE = /\\{/;",
                "export interface LedgerRowDto { id: string; }",
            ],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export interface LedgerRowDto { id: string; }"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1, msg=problems)

    def test_ignores_a_declaration_inside_a_line_comment(self):
        self.fixture.write_module(
            "workstation-2",
            ["// export interface LedgerRowDto { id: string; }", "export type Tone = 'Info';"],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0)

    def test_ignores_a_declaration_inside_a_template_literal(self):
        self.fixture.write_module(
            "workstation-2",
            ["export const sample = `", "export interface LedgerRowDto { id: string; }", "`;"],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0)

    def test_recognises_every_exported_declaration_form(self):
        self.fixture.write_module(
            "workstation-2",
            [
                "export type Tone = 'Info';",
                "export interface Row { id: string; }",
                "export enum Status { Open }",
                "export const DEFAULT_TONE: Tone = 'Info';",
                "export function toTone(value: string): Tone { return 'Info'; }",
                "export class ToneMap {}",
            ],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["exported_names"], 7)


class RepositoryBarrelTests(unittest.TestCase):
    def test_repository_barrel_has_no_duplicate_declarations(self):
        problems, counts = MODULE.evaluate(MODULE.BARREL_PATH, MODULE.TYPES_DIR)

        self.assertEqual(problems, [])
        self.assertEqual(counts["duplicates"], 0)
        self.assertGreater(counts["exported_names"], 0)


if __name__ == "__main__":
    unittest.main()
