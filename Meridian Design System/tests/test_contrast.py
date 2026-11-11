import importlib.util
import contextlib
import io
import tempfile
import subprocess
import sys
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


if __name__ == "__main__":
    unittest.main()
