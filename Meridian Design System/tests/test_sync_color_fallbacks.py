import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("sync_color_fallbacks", ROOT / "scripts/sync_color_fallbacks.py")
sync = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = sync
SPEC.loader.exec_module(sync)


class FallbackPlanTests(unittest.TestCase):
    def setUp(self):
        self.tokens = {
            "--ink": "#112233", "--alias": "var(--ink)",
            "--mixed": "color-mix(in srgb, #000000 75%, #FFFFFF)",
            "--wash": "color-mix(in srgb, var(--ink) 10%, transparent)",
            "--outline": "2px solid var(--ink)",
        }

    def test_only_color_literal_changes_with_spacing_and_role_preserved(self):
        text = 'color: "var( --alias ,  #abc  )"; border: 2px solid var(--mixed,#123456);\r\n'
        result = sync.plan_text(text, self.tokens)
        self.assertEqual(result["text"], 'color: "var( --alias ,  #112233  )"; border: 2px solid var(--mixed,#404040);\r\n')
        self.assertEqual(result["replacements"], 2)
        self.assertEqual([f["token"] for f in result["findings"]], ["--alias", "--mixed"])

    def test_equivalent_short_hex_and_checker_rounding_tolerance_are_unchanged(self):
        text = 'var(--white,#fff) var(--ink,#112234)'
        result = sync.plan_text(text, {**self.tokens, "--white": "#FFFFFF"})
        self.assertEqual(result["text"], text)
        self.assertEqual(result["replacements"], 0)

    def test_comments_preserved_and_not_rewritten(self):
        text = '''/* var(--ink,#000) */
// var(--ink,#000)
<!-- var(--ink,#000) -->
const color = "var(--ink,#000)"; // var(--ink,#000)
const external = "https://example.invalid/var(--ink,#000)";
'''
        result = sync.plan_text(text, self.tokens)
        self.assertEqual(result["replacements"], 1)
        self.assertTrue(result["text"].startswith('/* var(--ink,#000) */\n// var(--ink,#000)\n<!-- var(--ink,#000) -->'))
        self.assertIn('const color = "var(--ink,#112233)"; // var(--ink,#000)', result["text"])
        self.assertIn('https://example.invalid/var(--ink,#000)', result["text"])

    def test_jsx_prose_apostrophe_cannot_expose_a_comment_to_rewriting(self):
        text = "<p>Operator's view</p>\n// var(--ink,#000)\nconst color = 'var(--ink,#000)';"
        result = sync.plan_text(text, self.tokens)
        self.assertEqual(result["replacements"], 1)
        self.assertIn("// var(--ink,#000)", result["text"])

    def test_nested_chains_alpha_and_composite_geometry_are_preserved(self):
        text = '''var(--missing, var(--ink,#000))
var(--ink,rgba(0,0,0,.2)) var(--ink,#0008) var(--outline,2px solid #000)
var(--wash,#000)'''
        result = sync.plan_text(text, self.tokens)
        self.assertEqual(result["text"], '''var(--missing, var(--ink,#112233))
var(--ink,rgba(17,34,51,.2)) var(--ink,#11223388) var(--outline,2px solid #112233)
var(--wash,#000)''')
        self.assertEqual(result["replacements"], 4)
        self.assertIn("nested-chain-outer-role", {f["reason"] for f in result["findings"]})
        self.assertIn("alpha-kind-mismatch", {f["reason"] for f in result["findings"]})

    def test_nested_literal_belongs_to_inner_role(self):
        result = sync.plan_text('var(--outer,var(--ink,#000))', {**self.tokens, "--outer": "#FF0000"})
        self.assertEqual(result["text"], 'var(--outer,var(--ink,#112233))')
        self.assertEqual([f["token"] for f in result["findings"] if f["action"] == "replace"], ["--ink"])

    def test_wash_changes_hue_while_preserving_authored_alpha(self):
        tokens = {"--wash": "color-mix(in srgb, #2E7655 10%, transparent)"}
        text = 'var(--wash,rgba(78, 143, 92, .07)) var(--wash,#00000044)'
        result = sync.plan_text(text, tokens)
        self.assertEqual(result["text"], 'var(--wash,rgba(46, 118, 85, .07)) var(--wash,#2E765544)')

    def test_canonical_alpha_is_explicit_and_uses_declared_hue_and_alpha(self):
        tokens = {"--green": "#2E7655", "--wash": "color-mix(in srgb, var(--green) 10%, transparent)",
                  "--alias": "var(--wash)"}
        text = 'var(--alias,#E9F2EC) var(--wash,rgba(46,118,85,1))'
        self.assertEqual(sync.plan_text(text, tokens)["text"], text, "Default mode must not invent alpha for an opaque literal")
        result = sync.plan_text(text, tokens, canonical_alpha=True)
        self.assertEqual(result["text"], 'var(--alias,rgba(46, 118, 85, 0.1)) var(--wash,rgba(46, 118, 85, 0.1))')
        converted = [finding for finding in result["findings"] if finding["action"] == "replace"]
        self.assertEqual([finding["alpha"] for finding in converted], [0.1, 0.1])
        self.assertEqual([finding["hue"] for finding in converted], ["#2E7655", "#2E7655"])
        self.assertTrue(all(finding["reason"] == "canonical-alpha-wash" for finding in converted))
        self.assertEqual(sync.plan_text(result["text"], tokens, canonical_alpha=True)["replacements"], 0)

    def test_canonical_alpha_keeps_authored_translucency_and_composite_geometry(self):
        tokens = {"--wash": "color-mix(in srgb, #2E7655 10%, transparent)",
                  "--shadow": "0 1px 2px rgba(46,118,85,.1)"}
        text = 'var(--wash,rgba(0,0,0,.07)) var(--shadow,3px 4px 5px #E9F2EC)'
        result = sync.plan_text(text, tokens, canonical_alpha=True)
        self.assertEqual(result["text"], 'var(--wash,rgba(46,118,85,.07)) var(--shadow,3px 4px 5px #E9F2EC)')
        self.assertIn("alpha-kind-mismatch", {finding["reason"] for finding in result["findings"]})

    def test_composite_colors_follow_lexical_order_across_hex_and_rgba(self):
        tokens = {"--shadow": "0 1px 2px #123456, 0 2px 4px rgba(11,22,33,.3)"}
        text = 'var(--shadow,3px 4px 5px rgba(0,0,0,.2), -6px 7px 8px #00000044)'
        result = sync.plan_text(text, tokens)
        self.assertEqual(result["text"], 'var(--shadow,3px 4px 5px rgba(18,52,86,.2), -6px 7px 8px #0B162144)')
        self.assertEqual(result["replacements"], 2)

    def test_modern_rgb_and_percentage_alpha_keep_format(self):
        text = 'var(--ink,rgb(0 0 0 / 25%)) var(--white,rgba(0%, 0%, 0%, .15))'
        result = sync.plan_text(text, {**self.tokens, "--white": "#FFFFFF"})
        self.assertEqual(result["text"], 'var(--ink,rgb(17 34 51 / 25%)) var(--white,rgba(100%, 100%, 100%, .15))')

    def test_ambiguous_color_count_kind_and_malformed_functions_are_skipped(self):
        tokens = {**self.tokens, "--shadow": "0 1px #112233, 0 2px #445566"}
        for text in ['var(--shadow,0 1px #000)', 'var(--ink,2px solid #000)',
                     'var(--ink,rgba(0/0/0/.2))', 'var(--ink,rgb(0 0 0 .2))',
                     'var(--wash,#E9F2EC)']:
            with self.subTest(text=text):
                result = sync.plan_text(text, tokens)
                self.assertEqual(result["text"], text)
                self.assertEqual(result["replacements"], 0)

    def test_non_var_function_names_and_runtime_interpolation_are_unchanged(self):
        for text in ['customvar(--ink,#000)', 'var(--outline,${width}px solid #000)',
                     'var(--outline," + width + "px solid #000)']:
            with self.subTest(text=text):
                self.assertEqual(sync.plan_text(text, self.tokens)["text"], text)

    def test_same_name_and_transitive_local_overrides_are_preserved(self):
        for text in ['.panel { --ink:#123456; color:var(--ink,#000); }',
                     'const style = { "--ink": dynamic }; const color = "var(--alias,#000)";']:
            with self.subTest(text=text):
                result = sync.plan_text(text, self.tokens)
                self.assertEqual(result["text"], text)
                self.assertEqual(result["findings"][0]["reason"], "local-override")

    def test_unknown_cyclic_dynamic_and_channel_tokens_are_not_guessed(self):
        tokens = {"--cyclic": "var(--cyclic)", "--dynamic": "currentColor", "--channels": "210 60% 44%"}
        text = 'var(--unknown,#000) var(--cyclic,#000) var(--dynamic,#000) var(--channels,#000)'
        result = sync.plan_text(text, tokens)
        self.assertEqual(result["text"], text)
        self.assertEqual(result["replacements"], 0)

    def test_explicit_dark_contexts_and_test_sources_require_review(self):
        for text, path, reason in [
            (':root[data-theme="dark"] {color:var(--ink,#000)}', "screen.css", "explicit-dark-context"),
            ('@media(prefers-color-scheme: dark){color:var(--ink,#000)}', "screen.css", "explicit-dark-context"),
            ('expect(css).toContain("var(--ink,#000)")', "screen.test.tsx", "test-source"),
            ('# Shell comment var(--ink,#000)\necho "var(--ink,#000)"', "create-screen.sh", "shell-source"),
        ]:
            with self.subTest(path=path, text=text):
                result = sync.plan_text(text, self.tokens, path=path)
                self.assertEqual(result["text"], text)
                self.assertEqual(result["findings"][0]["reason"], reason)


class SynchronizeTests(unittest.TestCase):
    def test_dry_run_track_isolation_write_and_idempotence(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            root = repo / "Meridian Design System"
            (root / "tokens").mkdir(parents=True)
            (root / "tokens/theme.css").write_text(':root {--theme-ink:#112233;}', encoding="utf-8")
            (root / "tokens/colors.css").write_text(':root {--ink:#112233;--package-only:#123456;}', encoding="utf-8")
            (root / "components").mkdir()
            component = root / "components/Card.jsx"
            component.write_bytes(b'color:"var(--ink,#000)";\r\n')
            workstation = repo / "src/Meridian.Ui/dashboard/src"
            (workstation / "styles").mkdir(parents=True)
            (workstation / "styles/index.css").write_text(':root {--ink:#AABBCC;}', encoding="utf-8")
            screen = workstation / "screen.tsx"
            screen.write_text('"var(--ink,#000) var(--package-only,#000)"', encoding="utf-8")
            bundle = root / "_ds_bundle.js"
            bundle.write_text('var(--ink,#000)', encoding="utf-8")
            before = component.read_bytes()
            dry = sync.synchronize(root)
            self.assertEqual(component.read_bytes(), before, "Dry-run cannot mutate a consumer")
            self.assertEqual(dry["replacements"], 2)
            self.assertEqual(dry["skipped"]["undeclared-token"], 1, "Package declarations must not leak into the workstation track")
            result = sync.synchronize(root, write=True)
            self.assertEqual(result["replacements"], 2)
            self.assertEqual(component.read_bytes(), b'color:"var(--ink,#112233)";\r\n')
            self.assertEqual(screen.read_text(encoding="utf-8"), '"var(--ink,#AABBCC) var(--package-only,#000)"')
            self.assertEqual(bundle.read_text(encoding="utf-8"), 'var(--ink,#000)', "Generated bundles are rebuilt separately")
            self.assertEqual(sync.synchronize(root)["replacements"], 0, "A second run is idempotent")


if __name__ == "__main__":
    unittest.main()
