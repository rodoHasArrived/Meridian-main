import importlib.util
import contextlib
import io
import tempfile
import subprocess
import sys
import contextlib
import io
import re
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


class CssColorResolutionTest(unittest.TestCase):
    def test_hsl_channels_aliases_and_fallbacks_match_authored_colors(self):
        tokens = {"--primary": "208 17% 19%", "--label": "var(--inverse)", "--inverse": "43 26% 95%"}
        color = mod.resolve("hsl(var(--primary))", tokens)
        self.assertAlmostEqual(color[0], 40.2135, places=3)
        self.assertAlmostEqual(color[2], 56.6865, places=3)
        self.assertEqual(mod.resolve("var(--missing, rgb(100% 100% 100%))", tokens), (255., 255., 255.))
        self.assertGreater(mod.ratio(mod.resolve("hsl(var(--label))", tokens), color), 10)

    def test_cycles_missing_tokens_and_unsupported_required_colors_are_unresolved(self):
        for expression, tokens in [
            ("var(--missing)", {}),
            ("var(--a)", {"--a": "var(--b)", "--b": "var(--a)"}),
            ("var(--a)", {"--a": "var(--a, #fff)"}),
            ("hsl(var(--missing))", {}),
            ("color-mix(in oklab, #000, #fff)", {}),
        ]:
            with self.subTest(expression=expression, tokens=tokens):
                self.assertIsNone(mod.resolve_color(expression, tokens))
        self.assertEqual(mod.resolve("var(--a, #fff)", {"--a": "var(--a)"}), (255., 255., 255.))

    def test_color_mix_resolves_nested_aliases_both_weights_and_transparency(self):
        tokens = {"--ink": "#000", "--surface": "hsl(0 0% 100%)"}
        self.assertEqual(mod.resolve("color-mix(in srgb, var(--ink) 25%, var(--surface) 75%)", tokens), (191.25, 191.25, 191.25))
        self.assertEqual(mod.resolve_color("color-mix(in srgb, rgb(255 0 0) 50%, transparent)", {}), (255., 0., 0., .5))
        self.assertEqual(mod.resolve_color("color-mix(in srgb, #000 20%, #fff 20%)", {}), (127.5, 127.5, 127.5, .4))
        self.assertIsNone(mod.resolve("color-mix(in srgb, #000 50%, transparent)", {}))


class PrimaryActionContrastTest(unittest.TestCase):
    css = """
      @layer base {
        :root, [data-appearance="light"] {
          --primary: 208 17% 19%; --primary-foreground: 43 26% 95%;
          --brand-axis: #29323A; --surface: #F8F7F3; --bg-light: var(--surface);
          --ws-primary-hover: color-mix(in srgb,var(--brand-axis) 88%,var(--surface));
          --ws-primary-pressed: color-mix(in srgb,var(--brand-axis) 78%,var(--surface));
        }
        @media (prefers-color-scheme: dark) {
          :root { --primary: 43 20% 93%; --primary-foreground: 213 15% 15%;
                  --brand-axis: #F1EFEA; --surface: #171E25; }
        }
        :root[data-theme="dark"] {
          --primary: 43 20% 93%; --primary-foreground: 213 15% 15%;
          --brand-axis: #F1EFEA; --surface: #171E25;
        }
        :root[data-theme="light"] {
          --primary: 208 17% 19%; --primary-foreground: 43 26% 95%;
          --brand-axis: #29323A; --surface: #F8F7F3;
        }
      }
    """
    adapter = 'const buttonVariantClasses = {default: "bg-primary text-primary-foreground hover:bg-[var(--ws-primary-hover)] active:bg-[var(--ws-primary-pressed)]"};'

    def workstation_fixture(self, css=None, adapter=None):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        repo = Path(temporary.name)
        root = repo / "Meridian Design System"
        root.mkdir()
        index, button = repo / mod.WS_INDEX, repo / mod.WS_BUTTON
        index.parent.mkdir(parents=True)
        button.parent.mkdir(parents=True)
        index.write_text(self.css if css is None else css)
        button.write_text(self.adapter if adapter is None else adapter)
        return root

    def quiet(self, callback, *args):
        with contextlib.redirect_stdout(io.StringIO()):
            return callback(*args)

    def test_actual_monorepo_and_package_primary_states_pass(self):
        self.assertEqual(self.quiet(mod.check_package_primary, ROOT), [])
        self.assertEqual(self.quiet(mod.check_workstation, ROOT), [])

    def test_mode_scopes_keep_manual_and_forced_light_separate(self):
        contexts = mod.mode_tokens(self.css)
        self.assertEqual(contexts["light"]["--brand-axis"], "#29323A")
        self.assertEqual(contexts["auto-dark"]["--brand-axis"], "#F1EFEA")
        self.assertEqual(contexts["manual-dark"]["--brand-axis"], "#F1EFEA")
        self.assertEqual(contexts["forced-light"]["--brand-axis"], "#29323A")
        self.assertEqual(self.quiet(mod.check_workstation, self.workstation_fixture()), [])

    def test_hsl_base_regression_cannot_hide_behind_safe_brand_signal(self):
        css = self.css.replace("--primary: 208 17% 19%;", "--primary: 43 26% 95%;")
        css += ":root { --ws-accent: #C56343; --text-on-accent: #0D1217; }"
        failures = self.quiet(mod.check_workstation, self.workstation_fixture(css=css))
        self.assertTrue(any("light" in failure and "base label" in failure for failure in failures), failures)

    def test_each_hover_pressed_and_label_expression_is_required(self):
        for replacement in ("--ws-primary-hover: var(--missing);", "--ws-primary-pressed: hsl(var(--missing));"):
            css = self.css + ":root {" + replacement + "}"
            failures = self.quiet(mod.check_workstation, self.workstation_fixture(css=css))
            self.assertTrue(any("cannot resolve required background" in failure for failure in failures), failures)
        css = self.css.replace("--primary-foreground: 43 26% 95%;", "--primary-foreground: var(--missing);")
        failures = self.quiet(mod.check_workstation, self.workstation_fixture(css=css))
        self.assertTrue(any("cannot resolve required foreground" in failure for failure in failures), failures)

    def test_adapter_state_mapping_and_each_authored_scope_are_checked(self):
        changed = self.adapter.replace("--ws-primary-hover", "--missing-hover")
        failures = self.quiet(mod.check_workstation, self.workstation_fixture(adapter=changed))
        self.assertTrue(any("hover" in failure and "cannot resolve" in failure for failure in failures), failures)
        for prefix in ("hover:", "active:"):
            changed_label = self.adapter.replace('active:bg-[var(--ws-primary-pressed)]"',
                'active:bg-[var(--ws-primary-pressed)] ' + prefix + 'text-[var(--missing-label)]"')
            failures = self.quiet(mod.check_workstation, self.workstation_fixture(adapter=changed_label))
            self.assertTrue(any("cannot resolve required foreground" in failure for failure in failures), failures)
        css = self.css.replace('--primary-foreground: 213 15% 15%;', '--primary-foreground: 43 20% 93%;')
        failures = self.quiet(mod.check_workstation, self.workstation_fixture(css=css))
        self.assertTrue(any("auto-dark" in failure for failure in failures), failures)
        self.assertTrue(any("manual-dark" in failure for failure in failures), failures)
        self.assertFalse(any("forced-light" in failure for failure in failures), failures)

    def test_package_primary_rules_are_read_instead_of_assuming_token_pairs(self):
        states = mod.primary_css_states("""
            .mds-btn--primary { background: var(--brand-axis); color: var(--ink-inverse); }
            .mds-btn--primary:hover { background-color: var(--ink-inverse); }
        """)
        tokens = {"--brand-axis": "#29323A", "--ink-inverse": "#F5F3EE"}
        failures = self.quiet(mod.check_primary_states, "fixture", {"light": tokens}, states)
        self.assertTrue(any("hover label" in failure for failure in failures), failures)
        self.assertFalse(any("base label" in failure for failure in failures), failures)
        self.assertTrue(self.quiet(mod.check_primary_states, "fixture", {"light": tokens}, mod.primary_css_states("")))
        layered = mod.primary_css_states(".mds-btn--primary { background: #000; color: #fff; background-image: url(unknown.png); }")
        self.assertTrue(self.quiet(mod.check_primary_states, "fixture", {"light": tokens}, layered))

    def test_missing_consumer_inputs_fail_when_dashboard_is_present(self):
        root = self.workstation_fixture()
        (root.parent / mod.WS_BUTTON).unlink()
        self.assertTrue(self.quiet(mod.check_workstation, root))

    def test_actions_only_cli_checks_both_tracks_and_fails_missing_required_pairs(self):
        command = [sys.executable, str(ROOT / "scripts/check_contrast.py"), "--actions-only"]
        passing = subprocess.run(command, capture_output=True, text=True, check=False)
        self.assertEqual(passing.returncode, 0, passing.stderr)
        self.assertIn("package] structural primary", passing.stdout)
        self.assertIn("workstation] structural primary", passing.stdout)
        relative = subprocess.run([*command, "--root", "."], cwd=ROOT, capture_output=True, text=True, check=False)
        self.assertEqual(relative.returncode, 0, relative.stderr)
        self.assertIn("workstation] structural primary", relative.stdout)
        root = self.workstation_fixture(css=self.css.replace("--primary-foreground: 43 26% 95%;", "--primary-foreground: var(--missing);"))
        for relative in ("components/core/Button.jsx", "tokens/colors.css", "tokens/theme.css", "tokens/colors-dark.css"):
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text((ROOT / relative).read_text())
        failing = subprocess.run([*command, "--root", str(root)], capture_output=True, text=True, check=False)
        self.assertEqual(failing.returncode, 1)
        self.assertIn("cannot resolve required foreground", failing.stderr)
        self.assertNotIn("All primary action contrast pairs pass", failing.stdout)

    def test_monorepo_requires_consumer_sources_but_standalone_reports_scope(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "Meridian Design System"
            root.mkdir()
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(mod.check_workstation(root), [])
            self.assertIn("outside verification scope", output.getvalue())
            (root.parent / "Meridian.sln").write_text("fixture monorepo sentinel")
            failures = self.quiet(mod.check_workstation, root)
            self.assertTrue(any("required stylesheet or button adapter missing" in failure for failure in failures), failures)

    def test_translucent_primary_requires_and_uses_the_supporting_surface(self):
        states = {"base": ("#000", "rgba(255 255 255 / .5)")}
        self.assertTrue(self.quiet(mod.check_primary_states, "fixture", {"light": {}}, states))
        self.assertEqual(self.quiet(mod.check_primary_states, "fixture", {"light": {"--bg-light": "#fff"}}, states), [])


class LegacyGateCompatibilityTest(unittest.TestCase):
    def test_fallback_alias_gate_still_reports_drift_instead_of_crashing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "Meridian Design System"
            (root / "tokens").mkdir(parents=True)
            (root / "components").mkdir()
            (root / "tokens/colors.css").write_text(":root { --ink: #20252B; --text-primary: var(--ink); }")
            (root / "tokens/theme.css").write_text("")
            (root / "tokens/colors-dark.css").write_text("")
            (root / "components/Button.jsx").write_text("const css = `.label { color: var(--text-primary, #FFFFFF); }`;")
            with contextlib.redirect_stdout(io.StringIO()):
                failures = mod.check_fallbacks(root)
            self.assertEqual(len(failures), 1, failures)
            self.assertIn("token resolves to #20252B", failures[0])


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

    def test_auto_dark_brand_cascade_rejects_a_weaker_os_selector(self):
        dark = self.root / "tokens/colors-dark.css"
        content = dark.read_text(encoding="utf-8")
        content = re.sub(r"(@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*)[^{}]+\{", r"\1:root {", content, count=1)
        dark.write_text(content, encoding="utf-8")
        failures = self.quiet(mod.check_brands, self.root)
        self.assertTrue(any("auto-dark · brand indigo" in failure and "signal-fill label" in failure for failure in failures))

    def test_forced_light_preserves_brand_identity_instead_of_default_signal(self):
        dark = self.root / "tokens/colors-dark.css"
        with dark.open("a", encoding="utf-8") as out:
            out.write('\n:root[data-theme="light"] { --accent:#C56343; --text-on-accent:#0D1217; }\n')
        failures = self.quiet(mod.check_brands, self.root)
        self.assertTrue(any("forced-light · brand indigo" in failure and "identity is overridden" in failure for failure in failures))

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
        button = self.repo / mod.WS_BUTTON
        button.parent.mkdir(parents=True, exist_ok=True)
        button.write_text('const primary = "mds-btn--primary hover:bg-[var(--ws-accent)]";', encoding="utf-8")
        self.assertTrue(any("primary button paints an accent" in failure for failure in self.quiet(mod.check_workstation, self.root)))

    def test_defined_token_fallback_need_not_equal_light_color(self):
        self.write("components/Example.jsx", 'const CSS = `.example{color:var(--text-primary,#123456);}`;')
        self.assertEqual(self.quiet(mod.check_fallbacks, self.root), [])

    def test_missing_color_dependency_cannot_hide_behind_fallback(self):
        self.write("components/Example.jsx", 'const CSS = `.example{color:var(--nonexistent,#20252B);}`;')
        failures = self.quiet(mod.check_fallbacks, self.root)
        self.assertTrue(any("--nonexistent" in failure and "always active" in failure for failure in failures))

    def test_existing_known_gap_cannot_render_a_retired_fallback_palette(self):
        self.write("components/Example.jsx", 'const CSS = `.example{box-shadow:var(--shadow-raised,0 0 4px #06F3FF);}`;')
        failures = self.quiet(mod.check_fallbacks, self.root)
        self.assertTrue(any("--shadow-raised" in failure and "retired palette" in failure for failure in failures))

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
