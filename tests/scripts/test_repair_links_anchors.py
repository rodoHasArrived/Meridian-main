import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build" / "scripts" / "docs"
MODULE_PATH = SCRIPTS / "repair-links.py"

# The script imports sibling helpers by bare name, so its directory has to be
# importable before the spec is executed.
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

spec = importlib.util.spec_from_file_location("repair_links", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class HeadingToAnchorTests(unittest.TestCase):
    """GitHub's slugger does not collapse consecutive hyphens.

    Collapsing them made `_heading_to_anchor` disagree with GitHub for every
    heading that contains a stripped character between two spaces. The three
    deep links in `docs/product/implementation-todo-list.md` resolve correctly
    on github.com and were reported broken on that basis alone.
    """

    def test_em_dash_between_spaces_yields_a_double_hyphen(self) -> None:
        heading = "Tier 5 — W10: two rows await an operator session, one has an undefined criterion"
        self.assertEqual(
            module._heading_to_anchor(heading),
            "tier-5--w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion",
        )

    def test_dated_heading_keeps_the_double_hyphen(self) -> None:
        self.assertEqual(
            module._heading_to_anchor("W9-TRUTH-001 owner exception — 2026-09-26"),
            "w9-truth-001-owner-exception--2026-09-26",
        )

    def test_single_spaced_heading_is_unaffected(self) -> None:
        self.assertEqual(module._heading_to_anchor("Quick Commands"), "quick-commands")
        self.assertEqual(module._heading_to_anchor("Headline"), "headline")

    def test_authored_hyphens_in_heading_text_are_preserved(self) -> None:
        self.assertEqual(
            module._heading_to_anchor("Fail-closed marks"), "fail-closed-marks"
        )

    def test_punctuation_is_stripped_without_leaving_leading_hyphens(self) -> None:
        self.assertEqual(module._heading_to_anchor("`npm audit` gate"), "npm-audit-gate")

    def test_extracted_anchors_match_the_tracker_deep_links(self) -> None:
        """End-to-end: the real heading text resolves the real tracker anchors."""
        content = (
            "## Tier 5 — W10: two rows await an operator session, "
            "one has an undefined criterion\n"
        )
        anchors = module._extract_anchors(content)
        self.assertIn(
            "tier-5--w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion",
            anchors,
        )


if __name__ == "__main__":
    unittest.main()
