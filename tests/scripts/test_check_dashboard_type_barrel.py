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

    def test_overloads_in_one_module_are_not_a_duplicate(self):
        # Function overloads and declaration merging legitimately repeat a name inside one
        # module, and `export *` still publishes one unambiguous symbol. Counting each
        # occurrence made three signatures read as "3 barrel modules" and blocked CI.
        self.fixture.write_module(
            "workstation-1",
            [
                "export function load(id: string): string;",
                "export function load(id: number): string;",
                "export function load(id: unknown): string { return String(id); }",
            ],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)
        self.assertEqual(problems, [])

    def test_detects_a_duplicate_async_function_export(self):
        # The declaration pattern allowed modifiers only for `declare` and `abstract`, so an
        # `export async function` matched nothing and two modules publishing `load` collided
        # invisibly under the barrel.
        self.fixture.write_module(
            "workstation-1",
            ["export async function load(): Promise<void> {}"],
        )
        self.fixture.write_module(
            "workstation-2",
            ["export async function load(): Promise<void> {}"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("load" in p for p in problems), msg=problems)

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


class StarReexportTests(unittest.TestCase):
    """The exact scenario a bare `export *` hides, and the precondition that stops it."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_bare_star_reexport_in_a_barrel_module_fails(self):
        # Reviewer's case: workstation-1 republishes ../contracts, which declares LedgerRowDto,
        # while workstation-2 declares it directly. TypeScript drops the ambiguous name from
        # '@/types'. The lexer cannot resolve '../contracts', so instead of reporting zero
        # duplicates it refuses the construct and says which module to make explicit.
        self.fixture.write_module("workstation-1", ['export * from "../contracts";'])
        self.fixture.write_module("workstation-2", ["export interface LedgerRowDto { id: string; }"])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(
            any("workstation-1" in p and "../contracts" in p and "bare 'export *'" in p for p in problems),
            msg=problems,
        )

    def test_namespace_star_reexport_publishes_exactly_one_name(self):
        # `export * as Ledger from` publishes the single name `Ledger`, so it can collide with a
        # sibling's declaration of the same name — and it is resolvable, unlike the bare form.
        self.fixture.write_module("workstation-1", ['export * as Ledger from "../contracts";'])
        self.fixture.write_module("workstation-2", ["export interface Ledger { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("'Ledger'" in p and "2 barrel modules" in p for p in problems), msg=problems)
        self.assertFalse(any("bare 'export *'" in p for p in problems), msg=problems)

    def test_star_reexport_inside_a_string_or_comment_is_not_a_finding(self):
        self.fixture.write_module(
            "workstation-1",
            [
                '// export * from "../contracts";',
                'const doc = \'export * from "../contracts";\';',
                "export interface LedgerRowDto { id: string; }",
            ],
        )

        problems, _ = self.fixture.evaluate()

        self.assertFalse(any("bare 'export *'" in p for p in problems), msg=problems)


class MultipleDeclarationsPerLineTests(unittest.TestCase):
    """The `^` anchor saw only the first export on a line; brace depth now decides scope."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_second_declaration_on_a_line_is_collected(self):
        self.fixture.write_module(
            "workstation-1",
            ["export interface A { id: string; } export interface Shared { id: string; }"],
        )
        self.fixture.write_module("workstation-2", ["export interface Shared { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("'Shared'" in p for p in problems), msg=problems)

    def test_single_line_namespace_member_is_still_not_module_scope(self):
        # The anchor made this pass by accident — only `N` began the line. With the anchor gone,
        # scope has to come from brace depth at the match offset, or `Row` reads as top level and
        # collides with the sibling's real `Row`, blocking CI over a collision that cannot exist.
        self.fixture.write_module(
            "workstation-1",
            ["export namespace N { export interface Row { id: string; } }"],
        )
        self.fixture.write_module("workstation-2", ["export interface Row { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)
        self.assertEqual(problems, [])

    def test_export_inside_an_identifier_is_not_a_declaration(self):
        self.fixture.write_module(
            "workstation-1",
            ["const reexport = 1;", "export interface OnlyOne { id: string; }"],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["exported_names"], 2)


class TypeOnlyStarReexportTests(unittest.TestCase):
    """TypeScript 5.0 `export type *` publishes like its value form and must classify alike."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_type_only_bare_star_reexport_is_rejected(self):
        self.fixture.write_module("workstation-1", ['export type * from "../contracts";'])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(
            any("workstation-1" in p and "../contracts" in p and "bare 'export *'" in p for p in problems),
            msg=problems,
        )

    def test_type_only_namespace_star_reexport_publishes_one_name(self):
        self.fixture.write_module("workstation-1", ['export type * as Ledger from "../contracts";'])
        self.fixture.write_module("workstation-2", ["export interface Ledger { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertFalse(any("bare 'export *'" in p for p in problems), msg=problems)

    def test_star_reexport_without_surrounding_space_is_still_rejected(self):
        # `export*from"../contracts"` is legal JavaScript; requiring whitespace after the keyword
        # would let the tightest spelling through the rejection entirely.
        self.fixture.write_module("workstation-1", ['export*from"../contracts";'])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(any("bare 'export *'" in p for p in problems), msg=problems)


class ReexportOriginTests(unittest.TestCase):
    """Two modules publishing one binding is legal; two bindings is the collision."""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_reexporting_a_siblings_declaration_is_not_a_duplicate(self):
        # TypeScript allows both star exports here: `Shared` resolves to one binding, so it stays
        # importable from '@/types'. Counting owners by publishing module reported a duplicate and
        # blocked CI, and the advice it gave ("re-export it") was what the module already did.
        self.fixture.write_module("workstation-1", ["export interface Shared { id: string; }"])
        self.fixture.write_module("workstation-2", ['export { Shared } from "./workstation-1";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)
        self.assertEqual(problems, [])

    def test_two_independent_declarations_are_still_a_duplicate(self):
        self.fixture.write_module("workstation-1", ["export interface Shared { id: string; }"])
        self.fixture.write_module("workstation-2", ["export interface Shared { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("'Shared'" in p for p in problems), msg=problems)

    def test_two_modules_reexporting_the_same_external_binding_agree(self):
        self.fixture.write_module("workstation-1", ['export { Row } from "../contracts";'])
        self.fixture.write_module("workstation-2", ['export { Row } from "../contracts";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)

    def test_same_published_name_from_different_bindings_is_a_duplicate(self):
        # Both publish `Row`, but from different sources — TypeScript drops the name.
        self.fixture.write_module("workstation-1", ['export { Row } from "../contracts";'])
        self.fixture.write_module("workstation-2", ['export { Row } from "../other-contracts";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1, msg=problems)

    def test_alias_of_a_different_binding_still_collides(self):
        # workstation-2 publishes `Shared`, but it is contracts' `Row`, not workstation-1's
        # `Shared` — a genuinely ambiguous name that must stay reported.
        self.fixture.write_module("workstation-1", ["export interface Shared { id: string; }"])
        self.fixture.write_module("workstation-2", ['export { Row as Shared } from "../contracts";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1, msg=problems)

    def test_local_reexport_without_a_from_clause_belongs_to_its_own_module(self):
        self.fixture.write_module(
            "workstation-1",
            ["interface Shared { id: string; }", "export { Shared };"],
        )
        self.fixture.write_module("workstation-2", ["export interface Shared { id: string; }"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1, msg=problems)


class NamespaceOriginAndCommentedBarrelTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_two_modules_reexporting_one_namespace_agree_on_its_origin(self):
        # NAMESPACE_REEXPORT matches through `from`, so re-checking for the keyword found nothing
        # and each module fell back to itself as the origin. TypeScript publishes one namespace
        # binding here, so reporting a collision blocked CI for a valid barrel.
        self.fixture.write_module("workstation-1", ['export * as Contracts from "./origin";'])
        self.fixture.write_module("workstation-2", ['export * as Contracts from "./origin";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)
        self.assertEqual(problems, [])

    def test_namespaces_of_different_targets_still_collide(self):
        self.fixture.write_module("workstation-1", ['export * as Contracts from "./origin";'])
        self.fixture.write_module("workstation-2", ['export * as Contracts from "./other";'])

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)

    def test_a_commented_out_barrel_entry_is_not_a_live_module(self):
        # TypeScript ignores the statement, so requiring './types/retired' to exist blocked CI
        # over a module that was deliberately removed along with its export.
        self.fixture.barrel.write_text(
            'export * from "./types/workstation-1";\n'
            "/*\n"
            'export * from "./types/retired";\n'
            "*/\n"
            'export * from "./types/workstation-2";\n',
            encoding="utf-8",
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["modules"], 2)
        self.assertFalse(any("retired" in p for p in problems), msg=problems)

    def test_a_live_barrel_entry_on_the_same_line_as_a_comment_still_counts(self):
        self.fixture.barrel.write_text(
            'export * from "./types/workstation-1"; // keep\n'
            'export * from "./types/workstation-2";\n',
            encoding="utf-8",
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["modules"], 2, msg=problems)


class SpecifierResolutionTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_a_quoted_comment_before_from_is_not_the_target(self):
        # Searching raw text from the closing brace picked the comment's string, so two modules
        # sharing that comment were given one origin and a real collision read as zero duplicates.
        self.fixture.write_module(
            "workstation-1",
            ['export { Shared } /* "../origins/common" */ from "../origins/a";'],
        )
        self.fixture.write_module(
            "workstation-2",
            ['export { Shared } /* "../origins/common" */ from "../origins/b";'],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)

    def test_single_quoted_barrel_entries_are_discovered(self):
        self.fixture.barrel.write_text(
            "export * from './types/workstation-1';\nexport * from './types/workstation-2';\n",
            encoding="utf-8",
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["modules"], 2, msg=problems)
        self.assertEqual(problems, [])

    def test_single_quoted_reexport_targets_resolve(self):
        self.fixture.write_module("workstation-1", ["export interface Shared { id: string; }"])
        self.fixture.write_module("workstation-2", ["export { Shared } from './workstation-1';"])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)


class BarrelSyntaxCoverageTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_type_only_barrel_entries_are_discovered(self):
        self.fixture.barrel.write_text(
            'export type * from "./types/workstation-1";\n'
            'export * from "./types/workstation-2";\n',
            encoding="utf-8",
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["modules"], 2, msg=problems)
        self.assertEqual(problems, [])

    def test_a_destructured_export_is_rejected_rather_than_uncollected(self):
        # `export const { Shared } = value` publishes bindings this lexer cannot enumerate, so
        # leaving them uncollected would report a real collision as zero duplicates.
        self.fixture.write_module("workstation-1", ["export const { Shared } = value;"])

        problems, _ = self.fixture.evaluate()

        self.assertTrue(
            any("workstation-1" in p and "destructured export" in p for p in problems),
            msg=problems,
        )

    def test_an_ordinary_const_export_is_unaffected(self):
        self.fixture.write_module("workstation-1", ["export const Shared = 1;"])

        problems, counts = self.fixture.evaluate()

        self.assertFalse(any("destructured" in p for p in problems), msg=problems)
        self.assertEqual(counts["duplicates"], 0)


class ImportedReexportOriginTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = BarrelFixture(Path(self._tmp.name))

    def test_a_local_export_of_an_imported_name_resolves_to_its_source(self):
        # TypeScript keeps `Shared` unambiguous here: both modules publish origin's binding.
        self.fixture.write_module(
            "workstation-1",
            ['import type { Shared } from "../origin";', "export type { Shared };"],
        )
        self.fixture.write_module("workstation-2", ['export { Shared } from "../origin";'])

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0, msg=problems)

    def test_an_aliased_import_resolves_to_the_original_binding(self):
        self.fixture.write_module(
            "workstation-1",
            ['import type { Row as Shared } from "../origin";', "export type { Shared };"],
        )
        self.fixture.write_module("workstation-2", ['export { Row as Shared } from "../origin";'])

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 0)

    def test_a_local_export_of_a_locally_declared_name_still_collides(self):
        self.fixture.write_module(
            "workstation-1",
            ["interface Shared { id: string; }", "export { Shared };"],
        )
        self.fixture.write_module("workstation-2", ["export interface Shared { id: string; }"])

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)

    def test_imports_from_different_sources_still_collide(self):
        self.fixture.write_module(
            "workstation-1",
            ['import type { Shared } from "../origin-a";', "export type { Shared };"],
        )
        self.fixture.write_module(
            "workstation-2",
            ['import type { Shared } from "../origin-b";', "export type { Shared };"],
        )

        _, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
