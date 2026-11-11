import importlib.util
import sys
import contextlib
import io
import shutil
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

class ContrastGuardRegressionTest(unittest.TestCase):
    """Deliberate failures prove modernization keeps real rendered-risk guards."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.root = self.repo / "Meridian Design System"
        shutil.copytree(ROOT / "tokens", self.root / "tokens")

    def quiet(self, check, *args, **kwargs):
        with contextlib.redirect_stdout(io.StringIO()):
            return check(*args, **kwargs)

    def write(self, relative, content):
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        return target

    def replace(self, relative, old, new):
        target = self.root / relative
        content = target.read_text(encoding="utf-8")
        self.assertIn(old, content)
        target.write_text(content.replace(old, new, 1), encoding="utf-8")

    def test_primary_action_uses_graphite_and_rejects_low_contrast(self):
        modes = mod.package_modes(self.root)
        self.assertEqual(self.quiet(mod.check_mode, "light", modes["light"]), [])
        broken = {**modes["light"], "--brand-axis": "#F8F7F3"}
        failures = self.quiet(mod.check_mode, "broken", broken)
        self.assertTrue(any("primary button label" in failure for failure in failures))

    def test_focus_and_link_thresholds_still_fail_bad_actual_roles(self):
        light = mod.package_modes(self.root)["light"]
        broken = {**light, "--focus": "#F8F7F3", "--link": "#F8F7F3"}
        failures = self.quiet(mod.check_mode, "broken", broken)
        self.assertTrue(any("focus ring" in failure for failure in failures))
        self.assertTrue(any("link text on card" in failure for failure in failures))

    def test_small_financial_state_text_keeps_aa_threshold(self):
        light = mod.package_modes(self.root)["light"]
        failures = self.quiet(mod.check_mode, "broken", {**light, "--severity-ready-fg": "var(--green)"})
        self.assertTrue(any("severity-ready chip" in failure for failure in failures))

    def test_brands_inherit_constant_focus_and_final_dark_roles(self):
        # A brand changes the signal; it need not copy unrelated focus or final dark roles.
        self.assertEqual(self.quiet(mod.check_brands, self.root), [])
        theme = self.root / "tokens/theme.css"
        with theme.open("a", encoding="utf-8") as out:
            out.write('\nhtml[data-brand="broken"] {\n --brand-axis: #F8F7F3;\n}\n')
        failures = self.quiet(mod.check_brands, self.root)
        self.assertTrue(any("brand broken" in failure and "primary button label" in failure for failure in failures))

    def test_workstation_uses_actual_primary_hover_not_accent_signal(self):
        target = self.repo / mod.WS_INDEX
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text('''
:root[data-theme="light"] {
 --brand-axis: #29323A; --ink-inverse: #F5F3EE; --ws-surface: #F8F7F3;
 --ws-primary-hover: color-mix(in srgb, var(--brand-axis) 88%, var(--ws-surface));
 --ws-primary-pressed: color-mix(in srgb, var(--brand-axis) 78%, var(--ws-surface));
 --ws-accent: #FFFFFF;
}
:root[data-theme="dark"] {
 --brand-axis: #F1EFEA; --ink-inverse: #20252B; --ws-surface: #171E25;
 --ws-primary-hover: color-mix(in srgb, var(--brand-axis) 88%, var(--ws-surface));
 --ws-primary-pressed: color-mix(in srgb, var(--brand-axis) 78%, var(--ws-surface));
}
''', encoding="utf-8")
        self.assertEqual(self.quiet(mod.check_workstation, self.root), [])
        target.write_text(target.read_text().replace("color-mix(in srgb, var(--brand-axis) 88%, var(--ws-surface))", "#F5F3EE", 1), encoding="utf-8")
        failures = self.quiet(mod.check_workstation, self.root)
        self.assertTrue(any("light · workstation" in failure and "hover" in failure for failure in failures))

    def test_defined_token_fallback_need_not_equal_light_color(self):
        self.write("components/Example.jsx", 'const CSS = `.example{color:var(--text-primary,#123456);}`;')
        self.assertEqual(self.quiet(mod.check_fallbacks, self.root), [])

    def test_missing_color_dependency_cannot_hide_behind_fallback(self):
        self.write("components/Example.jsx", 'const CSS = `.example{color:var(--nonexistent,#20252B);}`;')
        failures = self.quiet(mod.check_fallbacks, self.root)
        self.assertTrue(any("--nonexistent" in failure and "always active" in failure for failure in failures))

    def test_undeclared_bare_style_fails_while_negative_tests_and_lint_strings_do_not(self):
        source = self.write("components/Example.jsx", 'const CSS = `.example{color:var(--missing-bare);}`;')
        self.assertTrue(any("--missing-bare" in failure for failure in self.quiet(mod.check_var_chains, self.root)))
        source.unlink()
        self.write("components/Example.test.ts", 'expect(output).not.toContain("color:var(--negative-fixture)");')
        self.write("_ds_bundle.js", 'const normalize = value => value.replace(/var\\(.*?\\)/g, "var(--x)");')
        self.assertEqual(self.quiet(mod.check_var_chains, self.root), [])

    def test_cyclic_custom_properties_are_not_valid_declared_guards(self):
        colors = self.root / "tokens/colors.css"
        with colors.open("a", encoding="utf-8") as out:
            out.write('\n:root{--cycle-a:var(--cycle-b);--cycle-b:var(--cycle-a);}\n')
        failures = self.quiet(mod.check_var_chains, self.root)
        self.assertTrue(any("cyclic" in failure and "--cycle-a" in failure for failure in failures))

    def test_local_style_tokens_and_optional_geometry_fallbacks_remain_valid(self):
        self.write("components/Example.jsx", 'const CSS = `.example{--local-color:#123456;color:var(--local-color,#123456);height:var(--optional-height,24px);}`;')
        self.assertEqual(self.quiet(mod.check_fallbacks, self.root), [])

    def test_bundle_source_css_parity_still_rejects_stale_rules(self):
        self.write("components/Example.jsx", 'const CSS = `\n.example{color:var(--text-primary,#20252B);}\n`;')
        self.write("_ds_bundle.js", "// A stale shipped bundle has no current CSS rule\n")
        failures = self.quiet(mod.check_bundle_parity, self.root)
        self.assertTrue(any("Example.jsx" in failure and "not in _ds_bundle" in failure for failure in failures))

    def test_brand_primary_font_is_loaded_but_fallback_names_are_not_primary(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><text font-family="IBM Plex Sans, Inter, sans-serif">Meridian</text></svg>'
        self.write("assets/brand/probe.svg", svg)
        self.assertEqual(self.quiet(mod.check_brand_assets, self.root), [])
        self.replace("assets/brand/probe.svg", "IBM Plex Sans, Inter", "Inter, IBM Plex Sans")
        failures = self.quiet(mod.check_brand_assets, self.root)
        self.assertTrue(any("primary face Inter" in failure for failure in failures))

    def test_brand_retired_color_and_copy_parity_guards_remain(self):
        svg = '<svg xmlns="http://www.w3.org/2000/svg"><path fill="#06F3FF"/></svg>'
        self.write("assets/brand/probe.svg", svg)
        failures = self.quiet(mod.check_brand_assets, self.root)
        self.assertTrue(any("retired-identity" in failure for failure in failures))
        copy = self.repo / "src/Meridian.Ui/dashboard/src/assets/brand/probe.svg"
        copy.parent.mkdir(parents=True, exist_ok=True)
        copy.write_text('<svg xmlns="http://www.w3.org/2000/svg"><path fill="#29323A"/></svg>', encoding="utf-8")
        failures = self.quiet(mod.check_brand_assets, self.root)
        self.assertTrue(any("differs from the package copy" in failure for failure in failures))

    def test_native_guards_are_default_and_browser_scope_is_explicit(self):
        self.write("assets/brand/probe.svg", '<svg xmlns="http://www.w3.org/2000/svg"><text font-family="IBM Plex Sans">Meridian</text></svg>')
        native = self.repo / "src/Meridian.Wpf/Assets/Brand/probe.svg"
        native.parent.mkdir(parents=True, exist_ok=True)
        native.write_text('<svg xmlns="http://www.w3.org/2000/svg"><text font-family="Inter">Old</text></svg>', encoding="utf-8")
        self.assertTrue(any("Meridian.Wpf" in failure for failure in self.quiet(mod.check_brand_assets, self.root)))
        self.assertEqual(self.quiet(mod.check_brand_assets, self.root, scope="browser"), [])


if __name__ == "__main__":
    unittest.main()
