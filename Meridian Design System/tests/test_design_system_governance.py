import importlib.util
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
