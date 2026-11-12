import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "check_design_system_governance.py"

spec = importlib.util.spec_from_file_location("check_design_system_governance", SCRIPT)
governance = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = governance
spec.loader.exec_module(governance)


class DesignSystemGovernanceTests(unittest.TestCase):
    def run_on_files(self, files):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return governance.run_checks(root, baseline={})

    def test_clean_package_passes(self):
        # NOTE: this exercises the real, current tree (ROOT), not a synthetic fixture. If this
        # ever fails, that's real signal — triage each violation before touching the baseline.
        violations = governance.run_checks(ROOT)
        self.assertEqual([], violations)

    def test_missing_local_link_is_reported(self):
        violations = self.run_on_files({"index.html": '<a href="missing.html">Broken</a>'})
        self.assertTrue(any(v.code == "local-link" for v in violations))

    def test_css_imports_and_urls_are_resolved_from_their_own_document(self):
        violations = self.run_on_files({
            "preview/example.html": '<style>@import "./tokens/missing.css"; .x{background:url("missing.svg")}</style>',
            "styles/example.css": '@import "missing.css"; .x{background-image:url(../missing.svg)}',
        })
        links = [v for v in violations if v.code == "local-link"]
        self.assertEqual(len(links), 4)
        self.assertEqual({v.path for v in links}, {"preview/example.html", "styles/example.css"})

    def test_css_relative_assets_and_external_fragment_dynamic_urls_are_valid(self):
        violations = self.run_on_files({
            "preview/example.html": '<!-- <style>@import "missing-comment.css";</style><img src="missing-comment.svg"> --><style>@import "../styles/theme.css";.x{background:url(../assets/icon.svg)}</style>',
            "styles/theme.css": '/* @import "missing-comment.css"; */ .x{background:url(https://example.test/x.svg)} .y{background:url(data:image/svg+xml;base64,AAA)} .z{fill:url(#marker)} .d{background:url(${dynamic})}',
            "assets/icon.svg": '<svg></svg>',
        })
        self.assertFalse(any(v.code == "local-link" for v in violations))

    def test_quoted_css_asset_paths_with_spaces_and_html_entities_are_checked(self):
        violations = self.run_on_files({
            "preview/example.html": '<div style="background:url(&quot;../assets/my%20icon.svg&quot;)"></div><style>.x{background:url("missing icon.svg")}</style>',
            "assets/my icon.svg": '<svg></svg>',
        })
        links = [v for v in violations if v.code == "local-link"]
        self.assertEqual(len(links), 1)
        self.assertIn("missing icon.svg", links[0].detail)

    def test_local_upload_link_is_reported_even_when_file_exists(self):
        violations = self.run_on_files(
            {
                "index.html": '<img src="uploads/reference.png" alt="">',
                "uploads/reference.png": "local-only image",
            }
        )
        self.assertTrue(any(v.code == "local-upload-reference" for v in violations))

    def test_raw_hex_is_reported_outside_baseline(self):
        violations = self.run_on_files({"templates/new/index.html": "<style>.x{color:#FFFFFF}</style>"})
        self.assertTrue(any(v.code == "raw-hex" for v in violations))

    def test_raw_hex_inside_var_fallback_is_exempt(self):
        # The sanctioned "token with a hex fallback" pattern (TOKEN_REFERENCE.md) must not trip
        # the same rule that catches a genuinely hardcoded color.
        violations = self.run_on_files(
            {"components/core/Widget.jsx": ".w{border-color:var(--border,#D7DCE2);}"}
        )
        self.assertFalse(any(v.code == "raw-hex" for v in violations))

    def test_prose_comments_run_ids_and_token_definitions_are_not_style_usages(self):
        violations = self.run_on_files({
            "README.md": "Copper #A85436; run #1841. Historical border-radius:12px.",
            "theme.css": ":root{--accent:#A85436}/* .x{color:#fff} */",
            "preview.html": "<p>Run #1847 uses #A85436.</p>",
        })
        self.assertFalse(any(v.code in {"raw-hex", "raw-color", "large-radius"} for v in violations))

    def test_card_extension_does_not_exempt_its_rendered_styles(self):
        violations = self.run_on_files({"components/core/widget.card.html": '<div style="color:#fff">Text</div>'})
        self.assertTrue(any(v.code == "raw-hex" for v in violations))

    def test_svg_white_fill_in_component_source_is_a_real_style_usage(self):
        violations = self.run_on_files({"components/charts/Chart.jsx": 'export function Chart(){return <svg><text fill="#fff">12</text></svg>}'})
        self.assertTrue(any(v.code == "raw-hex" for v in violations))

    def test_large_radius_is_reported(self):
        violations = self.run_on_files({"components/core/Widget.jsx": "const css = `.w{border-radius:12px;}`;"})
        self.assertTrue(any(v.code == "large-radius" for v in violations))

    def test_legacy_workspace_name_is_reported(self):
        violations = self.run_on_files({"templates/new/index.html": "<main><h1>Governance</h1></main>"})
        self.assertTrue(any(v.code == "legacy-workspace" for v in violations))

    def test_template_entry_requires_document_contract(self):
        violations = self.run_on_files(
            {
                "templates/new/index.html": (
                    "<!DOCTYPE html><html><head><title>New</title></head><body><section>Content</section></body></html>"
                )
            }
        )
        self.assertTrue(any(v.code == "template-viewport" for v in violations))
        self.assertTrue(any(v.code == "template-main-landmark" for v in violations))
        self.assertTrue(any(v.code == "template-heading" for v in violations))

    def test_template_entry_accepts_document_contract(self):
        violations = self.run_on_files(
            {
                "templates/new/index.html": (
                    '<!DOCTYPE html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">'
                    "</head><body><main><h1>New</h1>Content</main></body></html>"
                )
            }
        )
        self.assertFalse(any(v.code.startswith("template-") for v in violations))

    def test_template_entry_follows_x_import_to_sibling_screen(self):
        violations = self.run_on_files(
            {
                "templates/new/New.dc.html": (
                    '<!DOCTYPE html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">'
                    '<script src="./support.js"></script></head><body><x-dc>'
                    '<helmet><script src="./ds-base.js"></script></helmet>'
                    '<x-import component-from-global-scope="NewScreen" from="./screen.jsx" hint-size="100%,800px">'
                    "</x-import></x-dc></body></html>"
                ),
                "templates/new/screen.jsx": (
                    "function NewScreen() { return (<main><h1>New</h1></main>); }\n"
                    "window.NewScreen = NewScreen;"
                ),
            }
        )
        self.assertFalse(any(v.code.startswith("template-") for v in violations))

    def test_template_entry_x_import_missing_landmark_in_sibling_is_reported(self):
        violations = self.run_on_files(
            {
                "templates/new/New.dc.html": (
                    '<!DOCTYPE html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">'
                    "</head><body><x-dc>"
                    '<x-import component-from-global-scope="NewScreen" from="./screen.jsx" hint-size="100%,800px">'
                    "</x-import></x-dc></body></html>"
                ),
                "templates/new/screen.jsx": (
                    "function NewScreen() { return (<div>No landmark or heading</div>); }\n"
                    "window.NewScreen = NewScreen;"
                ),
            }
        )
        self.assertTrue(any(v.code == "template-main-landmark" for v in violations))
        self.assertTrue(any(v.code == "template-heading" for v in violations))

    def test_wrapper_landmarks_are_preserved_when_a_child_is_imported(self):
        violations = self.run_on_files({
            "templates/new/New.dc.html": '<meta name="viewport" content="width=device-width, initial-scale=1"><main><h1>New</h1><x-import from="./row.jsx"></x-import></main>',
            "templates/new/row.jsx": "function Row(){return <section>Rows</section>}",
        })
        self.assertFalse(any(v.code.startswith("template-") for v in violations))

    def test_multiple_template_headings_and_main_landmarks_are_rejected(self):
        violations = self.run_on_files({
            "templates/new/index.html": '<meta name="viewport" content="width=device-width, initial-scale=1"><main><h1>One</h1></main><main><h1>Two</h1></main>'
        })
        self.assertTrue(any(v.code == "template-main-landmark" for v in violations))
        self.assertTrue(any(v.code == "template-heading" for v in violations))

    def test_non_template_html_is_not_held_to_document_contract(self):
        # A component .card.html doc doesn't need to look like a workstation screen.
        violations = self.run_on_files({"components/core/widget.card.html": "<p>demo</p>"})
        self.assertFalse(any(v.code.startswith("template-") for v in violations))

    def test_missing_prompt_md_is_reported(self):
        violations = self.run_on_files(
            {
                "components/core/Widget.jsx": "export function Widget() {}",
                "components/core/Widget.d.ts": "export declare function Widget(): JSX.Element;",
            }
        )
        self.assertTrue(any(v.code == "prompt-coverage" for v in violations))

    def test_component_with_prompt_md_passes_coverage(self):
        violations = self.run_on_files(
            {
                "components/core/Widget.jsx": "export function Widget() {}",
                "components/core/Widget.d.ts": "export declare function Widget(): JSX.Element;",
                "components/core/Widget.prompt.md": "Widget \u2014 does a thing.",
            }
        )
        self.assertFalse(any(v.code == "prompt-coverage" for v in violations))

    def test_missing_style_tooling_fails_closed(self):
        with patch.object(governance.subprocess, "run", side_effect=FileNotFoundError("node unavailable")):
            violations = governance.check_rendered_styles(ROOT, [])
        self.assertEqual([v.code for v in violations], ["style-policy-unavailable"])

    def test_malformed_style_diagnostics_fail_closed(self):
        class MalformedResult:
            returncode = 0
            stdout = '{"unexpected":"shape"}'
            stderr = ""
        with patch.object(governance.subprocess, "run", return_value=MalformedResult()):
            violations = governance.check_rendered_styles(ROOT, [])
        self.assertEqual([v.code for v in violations], ["style-policy-unavailable"])


if __name__ == "__main__":
    unittest.main()
