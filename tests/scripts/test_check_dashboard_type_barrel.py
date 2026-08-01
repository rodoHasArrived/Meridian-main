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

    def test_detects_a_duplicate_among_indented_declarations(self):
        # Exports nested in a `declare module` block are still exports; requiring column zero
        # hid 11 real declarations and any duplicate of them.
        self.fixture.write_module(
            "workstation-1",
            ["declare module 'external' {", "  export interface NestedDto { id: string; }", "}"],
        )
        self.fixture.write_module(
            "workstation-2",
            ["declare module 'other' {", "  export interface NestedDto { id: string; }", "}"],
        )

        problems, counts = self.fixture.evaluate()

        self.assertEqual(counts["duplicates"], 1)
        self.assertTrue(any("NestedDto" in p for p in problems), msg=problems)

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
