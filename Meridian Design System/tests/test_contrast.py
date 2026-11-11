import importlib.util
import json
import sys
import re
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

    def test_css_var_fallback_resolution(self):
        self.assertEqual(mod.resolve("var(--missing, #FFFFFF)", {}), (255.0, 255.0, 255.0))
        self.assertEqual(mod.resolve("var(--ink, #FFFFFF)", {"--ink": "#000000"}), (0, 0, 0))

    def test_hsl_channel_resolution(self):
        self.assertEqual(mod.resolve("hsl(var(--primary))", {"--primary": "0 0% 0%"}), (0, 0, 0))
        self.assertEqual(mod.resolve("hsl(0 0% 100%)", {}), (255.0, 255.0, 255.0))


class TokenContrastTest(unittest.TestCase):
    def test_all_pairs_pass(self):
        failures = mod.run_checks(ROOT)
        self.assertEqual(failures, [], "Token contrast regressions:\n" + "\n".join(failures))


class SemanticChipContrastTest(unittest.TestCase):
    def test_actual_chip_roles_pass_all_appearance_scopes(self):
        light = mod.parse_tokens((ROOT / "tokens/colors.css").read_text(encoding="utf-8"))
        dark_css = mod.strip_comments((ROOT / "tokens/colors-dark.css").read_text(encoding="utf-8"))
        scopes = {
            "auto dark": r"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{([^{}]+)\}",
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
                target.write_text(text, encoding="utf-8")
            self.assertTrue(any("auto dark · brand indigo" in failure and "signal label" in failure
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
            button.write_text('default: "bg-primary text-primary-foreground hover:bg-[var(--ws-primary-hover)] active:bg-[var(--ws-primary-pressed)]"', encoding="utf-8")
            index.write_text(":root { --primary: 0 0% 0%; --primary-foreground: 0 0% 100%; --ws-primary-hover: #FFFFFF; --ws-primary-pressed: #222222; }", encoding="utf-8")
            failures = mod.check_workstation(root)
            self.assertEqual(len(failures), 1)
            self.assertIn("hover button label: 1.00:1", failures[0])


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
