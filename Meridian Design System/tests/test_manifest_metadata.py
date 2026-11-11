"""Source-derived metadata freshness without changing the public export contract."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("sync_manifest_metadata", ROOT / "scripts/sync_manifest_metadata.py")
SYNC = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SYNC)


class ManifestMetadataTests(unittest.TestCase):
    def setUp(self):
        self.manifest = {
            "namespace": "SyntheticPublicContract", "components": [{"name": "Public", "sourcePath": "components/Public.jsx"}],
            "startingPoints": ["keep"], "templates": [{"entryPath": "templates/Keep.dc.html"}],
            "themes": [{"selector": ':root[data-theme="dark"]', "label": "Dark"},
                       {"selector": 'html[data-brand="blue"]', "label": "Blue"}],
            "fonts": [{"family": "Public Font"}], "brandFonts": [{"family": "Brand Font"}],
            "cards": [{"path": "components/example.card.html", "name": "Old", "group": "Core",
                       "viewport": "100x100", "subtitle": "Stale", "custom": {"keep": True}}],
            "tokens": [
                {"name": "--canvas", "value": "#000000", "kind": "color", "definedIn": "tokens/colors.css"},
                {"name": "--gone", "value": "2px", "kind": "radius", "definedIn": "tokens/elevation.css"},
                {"name": "--theme-text", "value": "#000000", "kind": "font", "definedIn": "tokens/theme.css",
                 "scope": 'html[data-brand="blue"]'},
            ],
        }
        self.sources = {path: "/* intentionally empty fixture stylesheet */" for path in SYNC.TOKEN_FILES}
        self.sources["tokens/colors.css"] = ":root { --canvas: #F2F0EB; }"
        self.sources["components/example.card.html"] = '<!-- @dsCard name="Current" group="Core" viewport="1280x900" subtitle="Current subtitle" -->'

    def tokens(self, updated):
        return {(token["name"], token.get("scope")): token for token in updated["tokens"]}

    def test_stale_value_new_deleted_and_registered_scoped_tokens(self):
        self.sources["tokens/colors.css"] += "\n:root { --brand-signal-ink: #873D27; } .private { --private: #FFF; }"
        self.sources["tokens/theme.css"] = "html[data-brand='blue'] { --theme-text-on-accent: #FFFFFF; --amber: var(--orange); }"
        with self.assertRaisesRegex(ValueError, "metadata is stale"):
            SYNC.require_current_manifest(self.manifest, self.sources)
        updated = SYNC.synchronize_manifest(self.manifest, self.sources)
        tokens = self.tokens(updated)
        self.assertEqual("#F2F0EB", tokens[("--canvas", None)]["value"])
        self.assertNotIn(("--gone", None), tokens)
        self.assertNotIn(("--private", None), tokens)
        self.assertNotIn(("--theme-text", 'html[data-brand="blue"]'), tokens)
        self.assertEqual("color", tokens[("--brand-signal-ink", None)]["kind"])
        self.assertEqual("#FFFFFF", tokens[("--theme-text-on-accent", 'html[data-brand="blue"]')]["value"])
        self.assertEqual("color", tokens[("--amber", 'html[data-brand="blue"]')]["kind"])

    def test_existing_kinds_and_source_origins_survive_alphabetical_competitors(self):
        self.sources["tokens/colors-dark.css"] = ":root { --canvas: #11171D; }"
        self.sources["tokens/theme.css"] = "html[data-brand='blue'] { --theme-text: #FFFFFF; }"
        tokens = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))
        self.assertEqual("tokens/colors.css", tokens[("--canvas", None)]["definedIn"])
        self.assertEqual("#F2F0EB", tokens[("--canvas", None)]["value"])
        # Historical classification stays stable; a freshness refresh is not a kind migration.
        self.assertEqual("font", tokens[("--theme-text", 'html[data-brand="blue"]')]["kind"])

    def test_deleted_source_origin_falls_back_to_remaining_real_declaration(self):
        self.sources["tokens/colors.css"] = "/* previous source no longer declares canvas */"
        self.sources["tokens/theme.css"] = ":root { --canvas: #FCFBF8; }"
        token = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))[("--canvas", None)]
        self.assertEqual("tokens/theme.css", token["definedIn"])
        self.assertEqual("color", token["kind"])

    def test_unconditional_root_beats_earlier_media_but_conditional_only_is_retained(self):
        self.sources["tokens/colors.css"] = """
          @media (prefers-reduced-motion: reduce) { :root { --canvas: #000000; --motion: 0ms; } }
          :root { --canvas: #F2F0EB; --motion: 240ms; }
        """
        self.sources["tokens/colors-dark.css"] = """
          @supports (display: grid) { @media (prefers-color-scheme: dark) {
            :root { --auto-only: #11171D; }
            :root[data-theme='dark'] { --scope-ink: #F1EFEA; }
          } }
        """
        tokens = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))
        self.assertEqual("#F2F0EB", tokens[("--canvas", None)]["value"])
        self.assertEqual("240ms", tokens[("--motion", None)]["value"])
        self.assertEqual("#11171D", tokens[("--auto-only", None)]["value"])
        self.assertEqual("#F1EFEA", tokens[("--scope-ink", ':root[data-theme="dark"]')]["value"])

    def test_auto_dark_cannot_keep_an_old_origin_over_another_sources_light_default(self):
        self.manifest["tokens"][0].update({"definedIn": "tokens/colors-dark.css", "value": "#11171D"})
        self.sources["tokens/colors-dark.css"] = """
          @supports (display: grid) { @media (prefers-color-scheme: dark) {
            :root { --canvas: #11171D; --new-ink: #F1EFEA; }
          } }
          :root[data-theme='dark'] { --canvas: #11171D; }
        """
        self.sources["tokens/colors.css"] += " :root { --new-ink: #20252B; }"
        updated = SYNC.synchronize_manifest(self.manifest, self.sources)
        tokens = self.tokens(updated)
        self.assertEqual("#F2F0EB", tokens[("--canvas", None)]["value"])
        self.assertEqual("tokens/colors.css", tokens[("--canvas", None)]["definedIn"])
        self.assertEqual("#20252B", tokens[("--new-ink", None)]["value"])
        self.assertEqual("tokens/colors.css", tokens[("--new-ink", None)]["definedIn"])
        self.assertEqual("#11171D", tokens[("--canvas", ':root[data-theme="dark"]')]["value"])
        self.assertEqual(updated, SYNC.synchronize_manifest(updated, self.sources))

    def test_reduced_motion_only_token_keeps_its_conditional_fallback(self):
        self.sources["tokens/elevation.css"] = """
          @media (prefers-reduced-motion: reduce) { :root { --motion-only: 0ms; } }
        """
        token = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))[("--motion-only", None)]
        self.assertEqual("0ms", token["value"])
        self.assertEqual("tokens/elevation.css", token["definedIn"])

    def test_comments_strings_functions_and_unregistered_scopes_do_not_invent_tokens(self):
        self.sources["tokens/colors.css"] = '''
          /* :root { --comment-only: #BADBAD; } */
          :root { --canvas: #F2F0EB; --literal: "/* literal */; { }";
            --function: var(--fallback, "a;b"); }
          @media (width > 600px) { .private { --private: #000000; } }
        '''
        tokens = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))
        self.assertNotIn(("--comment-only", None), tokens)
        self.assertNotIn(("--private", None), tokens)
        self.assertEqual('"/* literal */; { }"', tokens[("--literal", None)]["value"])
        self.assertEqual('var(--fallback, "a;b")', tokens[("--function", None)]["value"])

    def test_card_quotes_entities_and_greater_than_are_not_truncated(self):
        self.sources["components/example.card.html"] = '''
          <!-- @dsCard name='Figure > frame' group="Charts" viewport="1400x920"
            subtitle="Estimate &gt; observed &quot;A&quot; &amp; B, complete" -->
        '''
        updated = SYNC.synchronize_manifest(self.manifest, self.sources)
        self.assertEqual("Figure > frame", updated["cards"][0]["name"])
        self.assertEqual('Estimate > observed "A" & B, complete', updated["cards"][0]["subtitle"])
        self.assertEqual({"keep": True}, updated["cards"][0]["custom"])

    def test_removed_optional_card_subtitle_drops_stale_value(self):
        self.sources["components/example.card.html"] = '<!-- @dsCard name="Current" group="Core" viewport="1280x900" -->'
        updated = SYNC.synchronize_manifest(self.manifest, self.sources)
        self.assertNotIn("subtitle", updated["cards"][0])

    def test_public_lists_are_preserved_inputs_unmodified_and_refresh_is_idempotent(self):
        original = copy.deepcopy(self.manifest)
        self.sources["components/not-public.card.html"] = '<!-- @dsCard name="Private" group="Core" viewport="1x1" -->'
        updated = SYNC.synchronize_manifest(self.manifest, self.sources)
        self.assertEqual(original, self.manifest)
        for key in self.manifest.keys() - {"tokens", "cards"}:
            self.assertEqual(self.manifest[key], updated[key], key)
        self.assertEqual([card["path"] for card in self.manifest["cards"]], [card["path"] for card in updated["cards"]])
        self.assertEqual(updated, SYNC.synchronize_manifest(updated, self.sources))
        SYNC.require_current_manifest(updated, {path: source.encode() for path, source in self.sources.items()})

    def test_first_declaration_wins_within_one_scope(self):
        self.sources["tokens/colors.css"] = ":root { --canvas: #F2F0EB; --canvas: #000000; }"
        tokens = self.tokens(SYNC.synchronize_manifest(self.manifest, self.sources))
        self.assertEqual("#F2F0EB", tokens[("--canvas", None)]["value"])

    def test_cli_check_write_and_second_write_do_not_mutate_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, source in self.sources.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(source, encoding="utf-8")
            path = root / "_ds_manifest.json"
            path.write_text(json.dumps(self.manifest), encoding="utf-8")
            command = [sys.executable, str(ROOT / "scripts/sync_manifest_metadata.py"), "--root", str(root)]
            self.assertEqual(1, subprocess.run(command + ["--check"], capture_output=True).returncode)
            self.assertEqual(0, subprocess.run(command + ["--write"], capture_output=True).returncode)
            written = path.read_bytes()
            self.assertEqual(0, subprocess.run(command + ["--check"], capture_output=True).returncode)
            self.assertEqual(0, subprocess.run(command + ["--write"], capture_output=True).returncode)
            self.assertEqual(written, path.read_bytes())
            for name, source in self.sources.items():
                self.assertEqual(source, (root / name).read_text(encoding="utf-8"))

    def test_missing_sources_and_malformed_css_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "Metadata source missing"):
            SYNC.synchronize_manifest(self.manifest, {})
        for source in [":root { --x: #000;", "/* never closed", ':root { --x: "unterminated; }']:
            with self.subTest(source=source), self.assertRaises(ValueError):
                SYNC.css_declarations(source)


if __name__ == "__main__":
    unittest.main()
