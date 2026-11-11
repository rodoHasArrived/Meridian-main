"""Tests for scripts/check_design_system_governance.py as shipped with 4.3.0.

The checker reports through module-level ``fails`` / ``warns`` lists against a module-level
``ROOT``. These tests point ``ROOT`` at a synthetic tree, run one gate, and read the lists.
The checker source is not modified by this file.
"""
import contextlib
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

TWEAKS = ("density", "theme", "environment", "dataState")


def template_entry(tweaks=TWEAKS, preview=True, switch=True, heading=True, routes=()):
    """A minimal entry that satisfies (or selectively breaks) the template contract."""
    props = {name: {"editor": "enum"} for name in tweaks}
    if preview:
        props = {"$preview": {"width": 1920, "height": 1080}, **props}
    encoded = json.dumps(props).replace('"', "&quot;")
    body = "<main><h1>New</h1></main>" if heading else "<main>Content</main>"
    if switch:
        body = f'<x-import component-from-global-scope="DS.DataStateSwitch">{body}</x-import>'
    script_routes = "".join(f'const r = "{route}";' for route in routes)
    return (
        "<!DOCTYPE html><html><head></head><body><x-dc>\n"
        '<!-- @template name="New" description="Synthetic" -->\n'
        f"{body}</x-dc>\n"
        f'<script type="text/x-dc" data-props="{encoded}">{script_routes}</script></body></html>'
    )


class GovernanceGateTests(unittest.TestCase):
    @contextlib.contextmanager
    def tree(self, files):
        """Run gates against a synthetic package root, restoring checker state afterwards."""
        saved = governance.ROOT, list(governance.fails), list(governance.warns)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "components").mkdir()
            (root / "templates").mkdir()
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            governance.ROOT = root
            governance.fails.clear()
            governance.warns.clear()
            try:
                yield governance
            finally:
                governance.ROOT = saved[0]
                governance.fails[:] = saved[1]
                governance.warns[:] = saved[2]

    def fails_for(self, gate, files, check):
        with self.tree(files) as checker:
            getattr(checker, check)()
            return [message for message in checker.fails if message.startswith(f"[{gate}]")]

    # ── the real tree ────────────────────────────────────────────────────────────────────────
    def test_clean_package_passes(self):
        # Exercises the real, current tree. A failure here is real signal: triage each finding
        # at its source. Open findings are tracked in issue #3505; do not relax the checker.
        saved = list(governance.fails), list(governance.warns)
        governance.fails.clear()
        governance.warns.clear()
        try:
            governance.check_components()
            governance.check_manifest()
            governance.check_templates()
            governance.check_eligibility()
            governance.check_generated()
            found = list(governance.fails)
        finally:
            governance.fails[:] = saved[0]
            governance.warns[:] = saved[1]
        self.assertEqual([], found, f"{len(found)} governance findings on the package tree")

    # ── trio ─────────────────────────────────────────────────────────────────────────────────
    def test_missing_prompt_md_is_reported(self):
        found = self.fails_for("trio", {
            "components/core/Widget.jsx": "export function Widget() {}",
            "components/core/Widget.d.ts": "export declare function Widget(): JSX.Element;",
        }, "check_components")
        self.assertEqual(1, len(found))
        self.assertIn("Widget.prompt.md", found[0])

    def test_missing_type_declaration_is_reported(self):
        found = self.fails_for("trio", {
            "components/core/Widget.jsx": "export function Widget() {}",
            "components/core/Widget.prompt.md": "Widget — does a thing.",
        }, "check_components")
        self.assertEqual(1, len(found))
        self.assertIn("Widget.d.ts", found[0])

    def test_component_with_full_trio_passes(self):
        found = self.fails_for("trio", {
            "components/core/Widget.jsx": "export function Widget() {}",
            "components/core/Widget.d.ts": "export declare function Widget(): JSX.Element;",
            "components/core/Widget.prompt.md": "Widget — does a thing.",
        }, "check_components")
        self.assertEqual([], found)

    def test_lowercase_helper_module_needs_no_trio(self):
        found = self.fails_for("trio", {"components/core/localDate.jsx": "export const x = 1;"},
                               "check_components")
        self.assertEqual([], found)

    # ── raw-hex ──────────────────────────────────────────────────────────────────────────────
    def test_raw_hex_is_reported(self):
        found = self.fails_for("raw-hex", {"components/core/Widget.jsx": ".w{color:#FFFFFF;}"},
                               "check_components")
        self.assertEqual(1, len(found))
        self.assertIn("Widget.jsx:1 bare #FFFFFF", found[0])

    def test_raw_hex_inside_var_fallback_is_exempt(self):
        # The sanctioned "token with a hex fallback" pattern must not trip the rule that catches
        # a genuinely hardcoded colour.
        found = self.fails_for(
            "raw-hex", {"components/core/Widget.jsx": ".w{border-color:var(--border,#D7DCE2);}"},
            "check_components")
        self.assertEqual([], found)

    # ── no-important ─────────────────────────────────────────────────────────────────────────
    def test_undeclared_important_is_reported(self):
        found = self.fails_for(
            "no-important", {"components/core/Widget.jsx": ".w{cursor:col-resize !important;}"},
            "check_components")
        self.assertEqual(1, len(found))

    def test_important_with_reason_pragma_is_accepted(self):
        found = self.fails_for("no-important", {
            "components/core/Widget.jsx":
                ".w{cursor:col-resize !important; /* important: must beat descendant cursors */}",
        }, "check_components")
        self.assertEqual([], found)

    def test_important_mentioned_only_in_a_comment_is_ignored(self):
        found = self.fails_for(
            "no-important", {"components/core/Widget.jsx": "// no !important needed here\n.w{}"},
            "check_components")
        self.assertEqual([], found)

    # ── aria-refs ────────────────────────────────────────────────────────────────────────────
    def test_hard_coded_aria_reference_is_reported(self):
        found = self.fails_for("aria-refs", {
            "components/core/Widget.jsx": '<div role="dialog" aria-labelledby="w-title"></div>',
        }, "check_components")
        self.assertEqual(1, len(found))
        self.assertIn('aria-labelledby="w-title"', found[0])

    def test_computed_aria_reference_is_accepted(self):
        found = self.fails_for("aria-refs", {
            "components/core/Widget.jsx": '<div role="dialog" aria-labelledby={titleId}></div>',
        }, "check_components")
        self.assertEqual([], found)

    # ── manifest ─────────────────────────────────────────────────────────────────────────────
    def test_manifest_entry_without_source_is_reported(self):
        found = self.fails_for("manifest", {
            "_ds_manifest.json": json.dumps(
                {"components": [{"name": "Gone", "sourcePath": "components/core/Gone.jsx"}]}),
        }, "check_manifest")
        self.assertEqual(["[manifest] manifest references missing components/core/Gone.jsx"], found)

    def test_missing_manifest_is_reported(self):
        found = self.fails_for("generated", {}, "check_manifest")
        self.assertEqual(1, len(found))

    def test_export_absent_from_manifest_is_a_warning(self):
        with self.tree({
            "_ds_manifest.json": json.dumps({"components": []}),
            "components/core/Widget.jsx": "export function Widget() {}",
        }) as checker:
            checker.check_manifest()
            self.assertEqual([], checker.fails)
            self.assertEqual(1, len(checker.warns))

    # ── template ─────────────────────────────────────────────────────────────────────────────
    def test_template_entry_accepts_complete_contract(self):
        with self.tree({
            "templates/new/New.dc.html": template_entry(),
            "templates/new/ds-base.js": "",
        }) as checker:
            checker.check_templates()
            self.assertEqual([], checker.fails)
            self.assertEqual([], checker.warns)

    def test_template_entry_must_be_named_after_its_folder(self):
        found = self.fails_for("template", {"templates/new-screen/index.html": template_entry()},
                               "check_templates")
        self.assertEqual(["[template] new-screen: entry NewScreen.dc.html missing"], found)

    def test_template_entry_requires_standard_tweaks_and_preview(self):
        found = self.fails_for("template", {
            "templates/new/New.dc.html": template_entry(tweaks=("density",), preview=False),
            "templates/new/ds-base.js": "",
        }, "check_templates")
        self.assertIn("[template] new: $preview missing from data-props", found)
        for tweak in ("theme", "environment", "dataState"):
            self.assertIn(f"[template] new: standard tweak '{tweak}' missing", found)
        self.assertNotIn("[template] new: standard tweak 'density' missing", found)

    def test_template_entry_requires_data_state_switch(self):
        found = self.fails_for("template", {
            "templates/new/New.dc.html": template_entry(switch=False),
            "templates/new/ds-base.js": "",
        }, "check_templates")
        self.assertEqual(["[template] new: DataStateSwitch not used"], found)

    def test_template_entry_requires_ds_base_sibling(self):
        found = self.fails_for("template", {"templates/new/New.dc.html": template_entry()},
                               "check_templates")
        self.assertEqual(["[template] new: ds-base.js missing"], found)

    def test_template_without_literal_heading_is_a_warning(self):
        with self.tree({
            "templates/new/New.dc.html": template_entry(heading=False),
            "templates/new/ds-base.js": "",
        }) as checker:
            checker.check_templates()
            self.assertEqual([], checker.fails)
            self.assertEqual(1, len(checker.warns))

    def test_scaffold_folder_is_not_held_to_template_contract(self):
        found = self.fails_for("template", {"templates/_scaffold/prefs.js": ""}, "check_templates")
        self.assertEqual([], found)

    # ── routes ───────────────────────────────────────────────────────────────────────────────
    def test_broken_template_route_is_reported(self):
        found = self.fails_for("routes", {
            "templates/new/New.dc.html": template_entry(routes=("../gone/Gone.dc.html",)),
            "templates/new/ds-base.js": "",
        }, "check_templates")
        self.assertEqual(["[routes] new: broken route ../gone/Gone.dc.html"], found)

    def test_resolving_template_route_is_accepted(self):
        found = self.fails_for("routes", {
            "templates/new/New.dc.html": template_entry(routes=("../other/Other.dc.html",)),
            "templates/new/ds-base.js": "",
            "templates/other/Other.dc.html": template_entry(),
            "templates/other/ds-base.js": "",
        }, "check_templates")
        self.assertEqual([], found)

    # ── eligibility ──────────────────────────────────────────────────────────────────────────
    def test_unclassified_template_is_reported(self):
        found = self.fails_for("eligibility", {
            "api/template-eligibility.json": json.dumps({"templates": {}}),
            "templates/new/New.dc.html": template_entry(),
        }, "check_eligibility")
        self.assertEqual(1, len(found))
        self.assertIn("new is not classified", found[0])

    def test_classification_of_a_removed_template_is_reported(self):
        found = self.fails_for("eligibility", {
            "api/template-eligibility.json": json.dumps({"templates": {"gone": {}}}),
        }, "check_eligibility")
        self.assertEqual(1, len(found))
        self.assertIn("classifies gone", found[0])

    def test_missing_eligibility_manifest_is_reported(self):
        found = self.fails_for("eligibility", {}, "check_eligibility")
        self.assertEqual(["[eligibility] api/template-eligibility.json missing"], found)

    # ── generated ────────────────────────────────────────────────────────────────────────────
    def test_missing_generated_files_are_reported(self):
        found = self.fails_for("generated", {"README.md": "no markers"}, "check_generated")
        for name in ("_ds_bundle.js", "_ds_manifest.json", "_adherence.oxlintrc.json", "support.js"):
            self.assertIn(f"[generated] {name} missing", found)
        self.assertIn("[generated] README.md inventory markers missing", found)


if __name__ == "__main__":
    unittest.main()
