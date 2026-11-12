import importlib.util
import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("check_contrast", ROOT / "scripts" / "check_contrast.py")
mod = importlib.util.module_from_spec(SPEC)
sys.modules["check_contrast"] = mod
SPEC.loader.exec_module(mod)


class ContrastMathTest(unittest.TestCase):
    def test_black_on_white_is_21(self):
        self.assertAlmostEqual(mod.ratio((0, 0, 0), (255, 255, 255)), 21.0, places=1)

    def test_var_resolution(self):
        tokens = {"--a": "#FFFFFF", "--b": "var(--a)"}
        self.assertEqual(mod.resolve("var(--b)", tokens), (255.0, 255.0, 255.0))

    def test_color_mix_resolution(self):
        tokens = {"--base": "#000000"}
        rgb = mod.resolve("color-mix(in srgb, var(--base) 75%, #FFFFFF)", tokens)
        self.assertAlmostEqual(rgb[0], 63.75, places=1)


class TokenContrastTest(unittest.TestCase):
    def test_all_pairs_pass(self):
        failures = mod.run_checks(ROOT)
        self.assertEqual(failures, [], "Token contrast regressions:\n" + "\n".join(failures))


class RuntimeVarChainTest(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory()
        self.addCleanup(self.workspace.cleanup)
        self.repo = Path(self.workspace.name)
        self.root = self.repo / "Meridian Design System"
        (self.root / "tokens").mkdir(parents=True)
        self.source = self.repo / "src/Meridian.Ui/dashboard/src"
        (self.source / "styles").mkdir(parents=True)
        (self.source / "styles/index.css").write_text(":root { --known: #FFFFFF; }", encoding="utf-8")

    def failures(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return mod.check_var_chains(self.root)

    def test_css_assertions_in_test_sources_are_not_runtime_call_sites(self):
        for filename in ("tailwind-build.test.ts", "chart.test.tsx", "chart.spec.ts", "chart.spec.tsx"):
            (self.source / filename).write_text(
                'expect(css).toContain("var(--generated-only-in-test)");', encoding="utf-8"
            )
        test_directory = self.source / "__tests__"
        test_directory.mkdir()
        (test_directory / "contract.ts").write_text(
            'expect(css).toContain("var(--assertion-only)");', encoding="utf-8"
        )
        (self.source / "chart.tsx").write_text(
            'export const style = { color: "var(--known)" };', encoding="utf-8"
        )

        self.assertEqual(self.failures(), [])

    def test_missing_runtime_token_still_fails_alongside_test_assertions(self):
        (self.source / "chart.test.ts").write_text(
            'expect(css).toContain("var(--generated-only-in-test)");', encoding="utf-8"
        )
        # A runtime component is not a test just because its name contains "test".
        (self.source / "backtest-chart.tsx").write_text(
            'export const style = { color: "var(--missing-runtime-color)" };', encoding="utf-8"
        )

        failures = self.failures()
        self.assertEqual(len(failures), 1)
        self.assertIn("backtest-chart.tsx:1", failures[0])
        self.assertIn("var(--missing-runtime-color)", failures[0])

    def test_package_components_and_explicit_bundle_remain_runtime_call_sites(self):
        components = self.root / "components"
        components.mkdir()
        component = components / "Chart.jsx"
        component.write_text('const color = "var(--missing-package-color)";', encoding="utf-8")
        bundle = self.root / "_ds_bundle.js"
        bundle.write_text('const color = "var(--missing-bundle-color)";', encoding="utf-8")

        failures = self.failures()
        self.assertEqual(len(failures), 2)
        self.assertTrue(any("Chart.jsx:1" in failure for failure in failures))
        self.assertTrue(any("_ds_bundle.js:1" in failure for failure in failures))


if __name__ == "__main__":
    unittest.main()
