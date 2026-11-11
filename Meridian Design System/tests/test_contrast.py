import importlib.util
import json
import contextlib
import io
import tempfile
import subprocess
import sys
import re
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

    def test_css_var_fallback_resolution(self):
        self.assertEqual(mod.resolve("var(--missing, #FFFFFF)", {}), (255.0, 255.0, 255.0))
        self.assertEqual(mod.resolve("var(--ink, #FFFFFF)", {"--ink": "#000000"}), (0, 0, 0))

    def test_hsl_channel_resolution(self):
        self.assertEqual(mod.resolve("hsl(var(--primary))", {"--primary": "0 0% 0%"}), (0, 0, 0))
        self.assertEqual(mod.resolve("hsl(0 0% 100%)", {}), (255.0, 255.0, 255.0))


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

    def test_forced_light_does_not_inherit_excluded_auto_dark_aliases(self):
        css = '''
:root { --theme-accent:#C56343; --accent:var(--theme-accent); }
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) { --theme-accent:var(--accent); --accent:#D88466; }
}
:root[data-theme="light"] { --accent:var(--theme-accent); }
'''
        contexts = mod.mode_tokens(css)
        self.assertEqual(mod.resolve("var(--accent)", contexts["auto-dark"]), (216., 132., 102.))
        self.assertEqual(mod.resolve("var(--accent)", contexts["forced-light"]), (197., 99., 67.))

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


class SemanticChipContrastTest(unittest.TestCase):
    def test_actual_chip_roles_pass_all_appearance_scopes(self):
        light = mod.parse_tokens((ROOT / "tokens/colors.css").read_text(encoding="utf-8"))
        dark_css = mod.strip_comments((ROOT / "tokens/colors-dark.css").read_text(encoding="utf-8"))
        scopes = {
            "auto dark": r'@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root(?::not\(\[data-theme="light"\]\))?\s*\{([^{}]+)\}',
            "manual dark": r':root\[data-theme="dark"\]\s*\{([^{}]+)\}',
            "forced light": r':root\[data-theme="light"\]\s*\{([^{}]+)\}',
        }
        modes = {"light": light}
        for name, pattern in scopes.items():
            match = re.search(pattern, dark_css, re.S)
            self.assertIsNotNone(match, f"The {name} appearance scope must remain measurable")
            modes[name] = {**light, **mod.parse_tokens(match.group(1))}
        for name, tokens in modes.items():
            with self.subTest(appearance=name):
                failures = mod.check_semantic_chips(name, tokens)
                self.assertEqual(failures, [], "Semantic chip contrast regressions:\n" + "\n".join(failures))

    def test_wash_is_composited_over_the_surface(self):
        tokens = {"--ink": "#000000", "--wash": "color-mix(in srgb, var(--ink) 10%, transparent)"}
        self.assertEqual(mod.composite("var(--wash)", tokens, (255, 255, 255)), (229.5, 229.5, 229.5))

    def test_role_check_rejects_unreadable_text(self):
        tokens = mod.parse_tokens((ROOT / "tokens/colors.css").read_text(encoding="utf-8"))
        tokens["--severity-ready-fg"] = "var(--bg-light)"
        self.assertTrue(any("severity-ready" in failure for failure in mod.check_semantic_chips("regression", tokens)))


class SourceRoleContrastTest(unittest.TestCase):
    def test_effective_brand_roles_pass_all_appearances(self):
        self.assertEqual(mod.check_brands(ROOT), [])

    def test_signal_text_role_rejects_an_unreadable_regression(self):
        tokens = mod.parse_tokens((ROOT / "tokens/colors.css").read_text(encoding="utf-8"))
        self.assertEqual(mod.check_signal_ink("light", tokens), [])
        tokens["--brand-signal-ink"] = "var(--accent-dim)"
        self.assertTrue(any("signal ink" in failure for failure in mod.check_signal_ink("regression", tokens)))

    def test_direct_white_brand_label_cannot_hide_behind_os_dark(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative in ["tokens/colors.css", "tokens/colors-dark.css", "tokens/theme.css", "components/core/Button.jsx"]:
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                text = (ROOT / relative).read_text(encoding="utf-8")
                if relative == "tokens/theme.css":
                    text = text.replace("--theme-text-on-accent: #FFFFFF", "--text-on-accent: #FFFFFF")
                    # Recreate the historical cascade before the stronger brand
                    # mappings were added; those mappings now prevent this defect.
                    text = text.split("/* ── A brand's accent family in dark and forced-light", 1)[0]
                if relative == "tokens/colors-dark.css":
                    # Reproduce the old weaker cascade, rather than weakening the
                    # current selector that intentionally excludes forced light.
                    text = text.replace(':root:not([data-theme="light"])', ':root')
                target.write_text(text, encoding="utf-8")
            self.assertTrue(any("auto dark · brand indigo" in failure and "signal label" in failure
                                for failure in mod.check_brands(root)))

    def test_declared_brand_dark_and_ghost_identities_reach_component_roles(self):
        theme = (ROOT / "tokens/theme.css").read_text(encoding="utf-8")
        sheets = "\n".join((ROOT / "tokens" / name).read_text(encoding="utf-8")
                           for name in ("colors.css", "theme.css", "colors-dark.css"))
        for brand, _ in mod.BRAND_BLOCK.findall(theme):
            for mode, tokens in mod.mode_tokens(sheets, brand=brand).items():
                prefix = "--theme-dark-" if mode in ("auto-dark", "manual-dark") else "--theme-"
                for suffix in ("accent", "accent-dim", "accent-hover", "accent-ghost", "text-on-accent"):
                    with self.subTest(brand=brand, mode=mode, role=suffix):
                        self.assertEqual(mod.resolve(f"var(--{suffix})", tokens),
                                         mod.resolve(f"var({prefix}{suffix})", tokens))

    def test_brand_identity_roles_cannot_silently_inherit_the_default(self):
        for role in ("--theme-accent-ghost", "--theme-dark-accent", "--theme-dark-accent-ghost"):
            with self.subTest(role=role), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                for relative in ("tokens/colors.css", "tokens/colors-dark.css", "tokens/theme.css", "components/core/Button.jsx"):
                    target = root / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / relative).read_text(encoding="utf-8")
                    if relative == "tokens/theme.css":
                        block = mod.BRAND_BLOCK.search(text)
                        self.assertEqual(block.group(1), "indigo")
                        body = re.sub(re.escape(role) + r"\s*:[^;]+;", "", block.group(2))
                        text = text[:block.start(2)] + body + text[block.end(2):]
                    target.write_text(text, encoding="utf-8")
                self.assertTrue(any(f"brand indigo] missing declared identity role {role}" in failure
                                    for failure in mod.check_brands(root)))

    def test_primary_button_checks_the_hover_fill(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "components/core/Button.jsx"
            source.parent.mkdir(parents=True)
            source.write_text("""
.mds-btn--primary{background:#000000;color:#FFFFFF;}
.mds-btn--primary:hover{background:#FFFFFF;}
.mds-btn--primary:active{background:#222222;}
""", encoding="utf-8")
            failures = mod.check_primary_buttons(root, "regression", {})
            self.assertEqual(len(failures), 1)
            self.assertIn(":hover: 1.00:1", failures[0])

    def test_brand_font_guard_reads_rendered_declarations(self):
        self.assertEqual(mod.svg_font_families('<svg><metadata>Inter</metadata><!-- Inter --><path d="M0 0"/></svg>'), set())
        families = mod.svg_font_families("""<svg xmlns="http://www.w3.org/2000/svg">
<!-- Historical outline source: Inter. -->
<metadata>Inter</metadata><path d="M0 0"/>
<text font-family="IBM Plex Sans, sans-serif">Current</text>
<style>.old { font-family: Inter, sans-serif; }</style>
<text style="font-family: 'JetBrains Mono', monospace">Legacy</text>
</svg>""")
        self.assertEqual(families, {"IBM Plex Sans", "sans-serif", "Inter", "JetBrains Mono", "monospace"})
        self.assertNotIn("IBM Plex Sans", mod.BRAND_FONTS)

    def test_workstation_checks_real_primary_roles(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "Meridian Design System"
            index = root.parent / mod.WS_INDEX
            button = root.parent / mod.WS_BUTTON
            index.parent.mkdir(parents=True)
            button.parent.mkdir(parents=True)
            button.write_text('const buttonVariantClasses = {default: "bg-primary text-primary-foreground hover:bg-[var(--ws-primary-hover)] active:bg-[var(--ws-primary-pressed)]"};', encoding="utf-8")
            index.write_text(":root { --primary: 0 0% 0%; --primary-foreground: 0 0% 100%; --ws-primary-hover: #FFFFFF; --ws-primary-pressed: #222222; }", encoding="utf-8")
            failures = mod.check_workstation(root)
            # An absent mode override inherits the same bad hover in every context.
            self.assertEqual(len(failures), 4)
            self.assertTrue(all("hover label: 1.00:1" in failure for failure in failures))


class WorkstationColourUtilityTest(unittest.TestCase):
    ARBITRARY = (
        "bg-[var(--brand-axis)] text-[var(--ink-inverse)] "
        "hover:bg-[color-mix(in_srgb,var(--brand-axis)_88%,var(--bg-light))] "
        "active:bg-[color-mix(in_srgb,var(--brand-axis)_78%,var(--bg-light))]"
    )

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "Meridian Design System"
        self.index = self.root.parent / mod.WS_INDEX
        self.button = self.root.parent / mod.WS_BUTTON
        self.index.parent.mkdir(parents=True)
        self.button.parent.mkdir(parents=True)
        self.index.write_text("""
:root { --brand-axis:#000000; --ink-inverse:#FFFFFF; --bg-light:#FFFFFF;
        --primary:0 0% 0%; --primary-foreground:0 0% 100%;
        --ws-primary-hover:#222222; --ws-primary-pressed:#333333; }
@media (prefers-color-scheme: dark) {
  :root { --brand-axis:#FFFFFF; --ink-inverse:#000000; --bg-light:#000000; }
}
:root[data-theme="dark"] { --brand-axis:#FFFFFF; --ink-inverse:#000000; --bg-light:#000000; }
:root[data-theme="light"] { --brand-axis:#000000; --ink-inverse:#FFFFFF; --bg-light:#FFFFFF; }
""", encoding="utf-8")

    def check_classes(self, classes):
        self.button.write_text(f'const buttonVariantClasses = {{default: "{classes}"}};', encoding="utf-8")
        return mod.check_workstation(self.root)

    def test_arbitrary_mix_classes_pass_in_all_four_appearances(self):
        self.assertEqual(self.check_classes(self.ARBITRARY), [])

    def test_legacy_hsl_utilities_and_token_state_fills_still_pass(self):
        self.assertEqual(self.check_classes(
            "bg-primary text-primary-foreground hover:bg-[var(--ws-primary-hover)] "
            "active:bg-[var(--ws-primary-pressed)]"), [])

    def test_arbitrary_unreadable_hover_is_measured(self):
        classes = self.ARBITRARY.replace(
            "hover:bg-[color-mix(in_srgb,var(--brand-axis)_88%,var(--bg-light))]",
            "hover:bg-[var(--ink-inverse)]")
        failures = self.check_classes(classes)
        self.assertEqual(len(failures), 4)
        self.assertTrue(all("hover label: 1.00:1" in failure for failure in failures))

    def test_state_label_override_is_measured(self):
        failures = self.check_classes(self.ARBITRARY + " hover:text-[var(--brand-axis)]")
        self.assertEqual(len(failures), 4)
        self.assertTrue(all("hover label" in failure for failure in failures))

    def test_unknown_expression_role_cannot_pass(self):
        failures = self.check_classes(self.ARBITRARY.replace("text-[var(--ink-inverse)]", "text-[var(--missing)]"))
        self.assertEqual(len(failures), 12)
        self.assertTrue(all("cannot resolve required foreground" in failure for failure in failures))

    def test_unknown_or_ambiguous_colour_utilities_cannot_pass(self):
        for classes, count, role in (
                (self.ARBITRARY.replace("bg-[var(--brand-axis)]", "bg-unknown", 1), 4, "background"),
                (self.ARBITRARY + " text-primary-foreground", 12, "foreground")):
            with self.subTest(classes=classes):
                failures = self.check_classes(classes)
                self.assertEqual(len(failures), count)
                self.assertTrue(all(f"cannot resolve required {role}" in failure for failure in failures))


class ContrastScopeTest(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "Meridian Design System"
        tokens = self.root / "tokens"
        tokens.mkdir(parents=True)
        (tokens / "colors.css").write_text(":root { --outer: #FFFFFF; --inner: #000000; --green: #2E7655; --wash: color-mix(in srgb, var(--green) 10%, transparent); --edge: #010203; --shadow: 0 2px 4px rgba(4,5,6,.18), 0 0 0 1px var(--edge); }", encoding="utf-8")
        (tokens / "theme.css").write_text("", encoding="utf-8")
        (tokens / "colors-dark.css").write_text("", encoding="utf-8")
        self.source = self.root / "components/Sample.jsx"
        self.source.parent.mkdir()

    def test_runtime_refs_survive_comment_masking_including_bundle(self):
        self.source.write_text('const url="https://example.com"; const detector=/https?:\\/\\//; const symbols=/[/*]+/; const css=".real{color:var(--missing-source)}"; // var(--comment)\n/* var(--block-comment) */', encoding="utf-8")
        (self.root / "_ds_bundle.js").write_text('/** Documentation example: var(--x). */\nconst css=".bundle{color:var(--missing-bundle)}";', encoding="utf-8")
        workstation = self.root.parent / "src/Meridian.Ui/dashboard/src"
        workstation.mkdir(parents=True)
        (workstation / "fixture.test.ts").write_text('const css="var(--test-only)";', encoding="utf-8")
        (workstation / "fixture.spec.tsx").write_text('const css="var(--spec-only)";', encoding="utf-8")
        (workstation / "runtime.ts").write_text('const css=".live{color:var(--missing-workstation)}";', encoding="utf-8")
        failures = mod.check_var_chains(self.root)
        self.assertEqual(len(failures), 3)
        for name in ("--missing-source", "--missing-bundle", "--missing-workstation"):
            self.assertTrue(any(name in failure for failure in failures))
        self.assertFalse(any("--test-only" in failure or "--spec-only" in failure or "--x)" in failure or "--comment" in failure for failure in failures))

    def test_nested_literals_belong_to_the_inner_role(self):
        self.source.write_text('const css=".x{color:var(--outer,var(--inner,#000000))}";', encoding="utf-8")
        self.assertEqual(mod.check_fallbacks(self.root), [])
        self.source.write_text('const css=".x{color:var(--outer,var(--inner,#FFFFFF))}";', encoding="utf-8")
        failures = mod.check_fallbacks(self.root)
        self.assertEqual(len(failures), 1)
        self.assertIn("var(--inner", failures[0])

    def test_composite_colours_keep_lexical_order(self):
        self.source.write_text('const css=".x{box-shadow:var(--shadow,0 2px 4px rgba(4,5,6,.2),0 0 0 1px #010203)}";', encoding="utf-8")
        self.assertEqual(mod.check_fallbacks(self.root), [])
        self.source.write_text('const css=".x{box-shadow:var(--shadow,0 2px 4px rgba(4,5,6,.2),0 0 0 1px #040506)}";', encoding="utf-8")
        self.assertTrue(mod.check_fallbacks(self.root))

    def test_translucent_role_rejects_opaque_hue_or_composited_hex(self):
        for fallback in ("#2E7655", "#E9F2EC", "rgb(46 118 85)", "#2E7655FF"):
            with self.subTest(fallback=fallback):
                self.source.write_text(f'const css=".x{{background:var(--wash,{fallback})}}";', encoding="utf-8")
                failures = mod.check_fallbacks(self.root)
                self.assertTrue(any("opaque fallback" in failure for failure in failures))

    def test_translucent_role_preserves_authored_alpha(self):
        for fallback in ("rgba(46,118,85,.17)", "rgb(46 118 85 / 10%)", "#2E76551A"):
            with self.subTest(fallback=fallback):
                self.source.write_text(f'const css=".x{{background:var(--wash,{fallback})}}";', encoding="utf-8")
                self.assertEqual(mod.check_fallbacks(self.root), [])

    def test_mixed_nested_and_owned_literals_need_scoped_review(self):
        self.source.write_text('const css=".x{box-shadow:var(--shadow,var(--inner,#000000) 0 0 #010203)}";', encoding="utf-8")
        failures = mod.check_fallbacks(self.root)
        self.assertTrue(any("mixes nested roles with owned literals" in failure for failure in failures))


class BundleInputContrastTest(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.inputs = ["components/Public.jsx", "components/private/Shared.jsx"]
        for name, selector in [("components/Public.jsx", "public"), ("components/private/Shared.jsx", "shared"),
                               ("components/Retired.jsx", "retired")]:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(f'const css = `\n.{selector}{{color:var(--text-primary);}}\n`;', encoding="utf-8")
        (self.root / "_ds_manifest.json").write_text(json.dumps({"components": [{"name": "Public", "sourcePath": self.inputs[0]}]}), encoding="utf-8")
        self.write_provenance(self.inputs)
        (self.root / "_ds_bundle.js").write_text('.public{color:var(--text-primary);}\n.shared{color:var(--text-primary);}', encoding="utf-8")

    def write_provenance(self, inputs):
        source_hashes = dict.fromkeys([*self.inputs, "components/Retired.jsx"], "retained-source-hash")
        (self.root / "release-build.json").write_text(json.dumps({"build": {"bundledSources": inputs, "sourceHashes": source_hashes}}), encoding="utf-8")

    def test_retained_nonpublic_css_is_not_expected_in_the_public_bundle(self):
        self.assertEqual(mod.check_bundle_parity(self.root), [])

    def test_stale_transitive_private_input_still_fails(self):
        (self.root / "_ds_bundle.js").write_text('.public{color:var(--text-primary);}', encoding="utf-8")
        failures = mod.check_bundle_parity(self.root)
        self.assertEqual(len(failures), 1)
        self.assertIn("components/private/Shared.jsx", failures[0])

    def test_public_source_must_be_a_compiler_input(self):
        self.write_provenance([self.inputs[1]])
        self.assertTrue(any("public source components/Public.jsx" in failure for failure in mod.check_bundle_parity(self.root)))

    def test_missing_input_metadata_requires_a_rebuild(self):
        (self.root / "release-build.json").write_text('{"build": {"sourceHashes": {}}}', encoding="utf-8")
        self.assertEqual(mod.check_bundle_parity(self.root), ["[bundle] bundled input inventory is missing; run npm run build"])


if __name__ == "__main__":
    unittest.main()
