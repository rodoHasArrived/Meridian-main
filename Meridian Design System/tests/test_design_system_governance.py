import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


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

    def test_comment_colors_are_ignored_without_hiding_real_declarations(self):
        violations = self.run_on_files(
            {"components/core/Widget.jsx": (
                "// Reference canvas #FFFFFF\n"
                "const css = `/* Old ink #000000 */ .w{color:var(--ink,#20252B);background:#FFFFFF;}`;"
            )}
        )
        colors = [v for v in violations if v.code == "raw-hex"]
        self.assertEqual(1, len(colors))
        self.assertEqual(2, colors[0].line)

    def test_quoted_urls_do_not_hide_a_later_color(self):
        violations = self.run_on_files(
            {"components/core/Widget.jsx": 'const url="https://example.invalid"; const css=`.w{color:#FFFFFF}`;'}
        )
        self.assertTrue(any(v.code == "raw-hex" for v in violations))

    def test_current_eight_pixel_dialog_radius_is_permitted(self):
        violations = self.run_on_files({"components/core/Widget.jsx": ".w{border-radius:8px;}"})
        self.assertFalse(any(v.code == "large-radius" for v in violations))

    def test_tokenized_pill_radius_is_permitted(self):
        violations = self.run_on_files({"components/core/Widget.jsx": ".w{border-radius:var(--radius-pill,999px);}"})
        self.assertFalse(any(v.code == "large-radius" for v in violations))

    def test_large_radius_is_reported(self):
        violations = self.run_on_files({"components/core/Widget.jsx": ".w{border-radius:12px;}"})
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

    def test_unmounted_sibling_and_comment_cannot_supply_heading(self):
        violations = self.run_on_files(
            {
                "templates/new/New.dc.html": '<meta name="viewport" content="width=device-width"><main><!-- <h1>Hidden</h1> --></main>',
                "templates/new/unmounted.jsx": "function Other(){return <h1>Unrelated</h1>}",
            }
        )
        self.assertTrue(any(v.code == "template-heading" for v in violations))

    def test_role_main_is_a_landmark(self):
        violations = self.run_on_files(
            {"templates/new/index.html": '<meta name="viewport" content="width=device-width"><div role="main"><h1>New</h1></div>'}
        )
        self.assertFalse(any(v.code == "template-main-landmark" for v in violations))

    def test_delegated_template_contract_follows_the_named_child(self):
        props = json.dumps({"$preview": {}, "theme": {}, "density": {}, "environment": {}, "dataState": {}}).replace('"', "&quot;")
        entry = (
            '<!-- @template name="New" --><meta name="viewport" content="width=device-width">'
            '<dc-import name="Child"></dc-import>'
            f'<script type="text/x-dc" data-props="{props}"></script>'
        )
        violations = self.run_on_files(
            {
                "templates/new/New.dc.html": entry,
                "templates/new/Child.dc.html": '<main><h1>Child</h1><x-import component-from-global-scope="DS.DataStateSwitch"></x-import></main>',
                "templates/new/ds-base.js": "",
            }
        )
        self.assertFalse(any(v.code.startswith("template") for v in violations))

    def test_unused_state_switch_name_does_not_satisfy_the_contract(self):
        violations = self.run_on_files(
            {"templates/new/New.dc.html": '<!-- @template name="New" --><main><h1>New</h1></main><script>const {DataStateSwitch}=DS;</script>'}
        )
        self.assertTrue(any("DataStateSwitch not mounted" in v.detail for v in violations))

    def test_declared_entry_filename_is_checked_without_a_pascal_case_assumption(self):
        violations = self.run_on_files(
            {
                "_ds_manifest.json": json.dumps({"templates": [{"entryPath": "templates/new/Named Entry.dc.html"}]}),
                "templates/new/Named Entry.dc.html": '<!-- @template name="New" --><meta name="viewport" content="width=device-width"><main><h1>New</h1></main>',
            }
        )
        self.assertFalse(any("No template entry" in v.detail or "Missing template entry" in v.detail for v in violations))

    def test_dynamic_links_are_not_missing_files(self):
        violations = self.run_on_files({"index.html": '<a href="{{ recordHref }}">Record</a>'})
        self.assertFalse(any(v.code == "local-link" for v in violations))

    def test_missing_manifest_in_a_release_is_reported(self):
        violations = self.run_on_files({"release.json": '{"version":"4.3.0"}'})
        self.assertTrue(any(v.code == "generated" and v.path == "_ds_manifest.json" for v in violations))

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


if __name__ == "__main__":
    unittest.main()
