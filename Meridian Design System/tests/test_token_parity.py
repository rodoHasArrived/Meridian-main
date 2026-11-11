import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("check_token_parity", ROOT / "scripts/check_token_parity.py")
parity = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(parity)


class TokenParityGraphTests(unittest.TestCase):
    def files(self):
        return {name: (ROOT / "tokens" / name).read_text(encoding="utf-8") for name in parity.CLOSURE}

    def test_current_values_and_theme_inputs_resolve_in_all_four_appearances(self):
        import json
        files = self.files()
        manifest = json.loads((ROOT / "tokens/parity-manifest.json").read_text(encoding="utf-8"))
        self.assertEqual([], parity.token_value_findings(manifest, files))
        self.assertEqual([], parity.theme_findings(files))

    def test_unguarded_os_dark_alias_recreates_the_forced_light_cycle(self):
        files = self.files()
        guarded = ':root:not([data-theme="light"])'
        self.assertIn(guarded, files["colors-dark.css"])
        files["colors-dark.css"] = files["colors-dark.css"].replace(guarded, ":root", 1)
        envs = parity.mode_envs([files[name] for name in parity.CLOSURE])
        self.assertIsNone(parity.resolve_token("--accent", envs["forced-light"]))
        self.assertIsNone(parity.resolve_token("--theme-accent", envs["forced-light"]))
        self.assertTrue(any("forced-light --theme-accent" in finding for finding in parity.theme_findings(files)))
        findings = parity.token_value_findings({"tokens": {"--accent": {"light": "#C56343", "dark": "#D88466"}}}, files)
        self.assertTrue(any("--accent forced-light" in finding and "cyclic" in finding for finding in findings))

    def test_a_forced_light_alias_can_reference_an_independent_theme_input(self):
        envs = parity.mode_envs([
            ':root { --theme-accent:#C56343; --accent:var(--theme-accent); }',
            '@media (prefers-color-scheme:dark) { :root:not([data-theme="light"]) { --theme-accent:var(--accent); --accent:#D88466; } }',
            ':root[data-theme="light"] { --accent:var(--theme-accent, #C56343); }',
        ])
        self.assertEqual("#c56343", parity.resolve_token("--accent", envs["forced-light"]))
        self.assertEqual("#d88466", parity.resolve_token("--accent", envs["auto"]))

    def test_os_dark_still_applies_to_forced_light_without_an_exclusion(self):
        envs = parity.mode_envs([
            ':root { --ink:#FFFFFF; }',
            '@media (prefers-color-scheme:dark) { :root { --ink:#000000; } }',
        ])
        self.assertEqual("#000000", parity.resolve_token("--ink", envs["forced-light"]))

    def test_a_dark_value_in_explicit_light_is_rejected(self):
        files = {"colors.css": ':root {--ink:#FFFFFF;}', "colors-dark.css": ':root[data-theme="light"] {--ink:#000000;}'}
        findings = parity.token_value_findings({"tokens": {"--ink": {"light": "#FFFFFF", "dark": "#FFFFFF"}}}, files)
        self.assertTrue(any("--ink forced-light" in finding for finding in findings))

    def test_specificity_beats_later_plain_root_and_print_is_separate(self):
        envs = parity.mode_envs([
            ':root[data-theme="light"] {--ink:#FFFFFF;}',
            ':root {--ink:#000000;} @media print { :root {--ink:#EEEEEE;} }',
        ])
        self.assertEqual("#ffffff", parity.resolve_token("--ink", envs["forced-light"]))
        self.assertEqual("#000000", parity.resolve_token("--ink", envs["light"]))

    def test_a_fallback_inside_a_cycle_does_not_make_the_cycle_valid(self):
        env = {"--a": "var(--b, #FFFFFF)", "--b": "var(--a)", "--outside": "var(--a, #123456)"}
        self.assertIsNone(parity.resolve_token("--a", env))
        self.assertIsNone(parity.resolve_token("--b", env))
        self.assertEqual("#123456", parity.resolve_token("--outside", env))

    def test_fallback_dependencies_participate_in_cycle_detection(self):
        env = {"--a": "var(--present, var(--b))", "--present": "#FFFFFF", "--b": "var(--a)"}
        self.assertEqual({"--a", "--b"}, parity.cyclic_tokens(env))
        self.assertIsNone(parity.resolve_token("--a", env))


    def test_function_fallback_does_not_hide_the_live_color_alias(self):
        env = {"--ink": "rgba(1,2,3,.4)", "--alias": "var(--ink, rgba(9,8,7,.6))"}
        self.assertEqual("rgba(1,2,3,.4)", parity.resolve_token("--alias", env))
        env["--ink"] = "rgba(20,30,40,.7)"
        self.assertEqual("rgba(20,30,40,.7)", parity.resolve_token("--alias", env))

    def test_nested_function_fallback_and_its_aliases_resolve(self):
        env = {"--ink": "#000000", "--alias": "var(--missing, color-mix(in srgb, var(--ink) 10%, transparent))"}
        self.assertEqual("color-mix(in srgb, #000000 10%, transparent)", parity.resolve_token("--alias", env))


if __name__ == "__main__":
    unittest.main()
