import datetime as dt
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-test-skip-register.py"
SPEC = importlib.util.spec_from_file_location("check_test_skip_register", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)

TODAY = dt.date(2026, 8, 1)

QUARANTINED_SOURCE = """using Xunit;

namespace Meridian.Tests.Example;

public sealed class ExampleTests
{
    [Fact(Skip = "Quarantined pending a product decision on the evidence-capture path.")]
    public void Disabled_Example()
    {
    }
}
"""

CONCATENATED_SOURCE = """using Xunit;

namespace Meridian.Tests.Example;

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!DockerAvailable)
        {
            Skip = "PostgreSQL tests are skipped because Docker is unavailable. " +
                "Start Docker to run them.";
        }
    }
}
"""


def entry(
    path: str,
    reason: str,
    test: str = "Disabled_Example",
    owner: str = "Storage",
    category: str = "quarantined",
    tracking: str = "PRD-112",
    review_by: str = "2027-01-01",
) -> dict:
    return {
        "path": path,
        "test": test,
        "reason": reason,
        "owner": owner,
        "category": category,
        "tracking": tracking,
        "review_by": review_by,
    }


class SkipRegisterFixture:
    """Minimal tests/ tree plus a register, for targeted mutation."""

    QUARANTINE_REASON = "Quarantined pending a product decision on the evidence-capture path."
    CONCATENATED_REASON = "PostgreSQL tests are skipped because Docker is unavailable. Start Docker to run them."
    QUARANTINE_PATH = "tests/Meridian.Tests/ExampleTests.cs"

    def __init__(self, root: Path) -> None:
        self.root = root
        self.tests_dir = root / "tests" / "Meridian.Tests"
        self.tests_dir.mkdir(parents=True)
        self.register_path = root / "register.json"

        (self.tests_dir / "ExampleTests.cs").write_text(QUARANTINED_SOURCE, encoding="utf-8")
        self.write_register([entry(self.QUARANTINE_PATH, self.QUARANTINE_REASON)])

    def write_register(self, entries: list[dict]) -> None:
        self.register_path.write_text(json.dumps({"skips": entries}), encoding="utf-8")

    def add_concatenated_skip(self) -> str:
        path = self.tests_dir / "DatabaseFactAttribute.cs"
        path.write_text(CONCATENATED_SOURCE, encoding="utf-8")
        return path.relative_to(self.root).as_posix()

    def sites(self) -> list:
        return MODULE.discover_skips(self.root / "tests", self.root)

    def problems(self, today: dt.date = TODAY) -> list[str]:
        entries = MODULE.load_register(self.register_path)
        return MODULE.evaluate(self.sites(), entries, today)


class DiscoverSkipsTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = SkipRegisterFixture(Path(self._tmp.name))

    def test_finds_attribute_skip(self):
        sites = self.fixture.sites()

        self.assertEqual(len(sites), 1)
        self.assertEqual(sites[0].reason, SkipRegisterFixture.QUARANTINE_REASON)
        self.assertEqual(sites[0].path, SkipRegisterFixture.QUARANTINE_PATH)

    def test_joins_concatenated_skip_reason(self):
        self.fixture.add_concatenated_skip()

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn(SkipRegisterFixture.CONCATENATED_REASON, reasons)

    def test_finds_interpolated_skip_reason(self):
        # An interpolated reason has no statically-known string, but the skip is just as real;
        # matching only plain literals left environment-gated skips entirely uninventoried.
        (self.fixture.tests_dir / "InterpolatedTests.cs").write_text(
            'public sealed class GatedFact : FactAttribute { public GatedFact() { Skip = $"skipped because {Variable}=true."; } }\n',
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn('$"skipped because {Variable}=true."', reasons)

    def test_finds_variable_skip_assignment_in_a_test_attribute(self):
        # DirectLendingDatabaseFactAttribute assigns a computed reason to its own Skip property.
        # There is no literal to record, but the tests it decorates really are disabled.
        (self.fixture.tests_dir / "GatedFactAttribute.cs").write_text(
            "public sealed class GatedFactAttribute : FactAttribute "
            "{ public GatedFactAttribute(string reason) { Skip = reason; } }\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("reason", reasons)

    def test_finds_a_constant_skip_reason_on_an_ordinary_fact(self):
        # `[Fact(Skip = SkipReasons.Quarantine)]` disables the test just as a literal does, but
        # the file only *uses* FactAttribute rather than deriving from one. Requiring a declared
        # custom attribute for computed expressions missed every skip of this shape.
        (self.fixture.tests_dir / "ConstantSkipTests.cs").write_text(
            "public sealed class T {\n"
            "    [Fact(Skip = SkipReasons.Quarantine)]\n"
            "    public void Disabled() { }\n"
            "}\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("SkipReasons.Quarantine", reasons)

    def test_finds_a_nameof_skip_reason_on_an_ordinary_fact(self):
        (self.fixture.tests_dir / "NameofSkipTests.cs").write_text(
            "public sealed class T {\n"
            "    [Theory(Skip = nameof(Disabled))]\n"
            "    public void Disabled() { }\n"
            "}\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("nameof(Disabled)", reasons)

    def test_ignores_a_skip_property_on_a_non_test_attribute(self):
        # Bracket depth alone accepted `[UiOption(Skip = "cursor")]` — an ordinary attribute with
        # a string property that happens to be named Skip — and demanded a register entry for it.
        # Being inside brackets is not being inside a test attribute.
        (self.fixture.tests_dir / "OptionTests.cs").write_text(
            "public sealed class T {\n"
            '    [UiOption(Skip = "cursor")]\n'
            "    public string Value { get; set; }\n"
            "}\n",
            encoding="utf-8",
        )

        paths = {site.path for site in self.fixture.sites()}

        self.assertNotIn("tests/Meridian.Tests/OptionTests.cs", paths)

    def test_finds_a_skip_on_a_custom_fact_attribute(self):
        # Custom gated attributes in this repository are all named *Fact/*Theory, so the
        # convention is what distinguishes them from unrelated attributes.
        (self.fixture.tests_dir / "GatedUseTests.cs").write_text(
            "public sealed class T {\n"
            '    [DatabaseFact(Skip = "Requires PostgreSQL.")]\n'
            "    public void Disabled_Db() { }\n"
            "}\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("Requires PostgreSQL.", reasons)

    def test_ignores_a_skip_property_on_an_ordinary_dto(self):
        # `Skip` is not reserved. Counting every assignment made the gate demand a register entry
        # for a pagination offset, so adding a test for any type with a Skip property blocked CI
        # with no test disabled anywhere.
        (self.fixture.tests_dir / "PagingTests.cs").write_text(
            "public sealed class T { void M() { var q = new SearchOptions { Skip = 10, Take = 5 }; } }\n",
            encoding="utf-8",
        )

        paths = {site.path for site in self.fixture.sites()}

        self.assertNotIn("tests/Meridian.Tests/PagingTests.cs", paths)

    def test_ignores_a_skip_example_inside_an_fsharp_block_comment(self):
        # The scan covers **/*.fs, whose comment syntax is (* ... *) and nests. Without it a
        # documentation example was inventoried as a real skipped test.
        (self.fixture.tests_dir / "DocumentedTests.fs").write_text(
            '(* Disable a case with (* nested *) [<Fact(Skip = "example reason")>] *)\n'
            "let value = 1\n",
            encoding="utf-8",
        )

        paths = {site.path for site in self.fixture.sites()}

        self.assertNotIn("tests/Meridian.Tests/DocumentedTests.fs", paths)

    def test_an_fsharp_type_variable_does_not_hide_a_later_skip(self):
        # 'T is a generic type parameter, not a character literal. Consuming to the next
        # apostrophe swallowed the real Skip below it and the gate reported full coverage
        # over an unregistered skipped test.
        (self.fixture.tests_dir / "GenericTests.fs").write_text(
            "let identity<'T> (value: 'T) : 'T = value\n"
            '[<Fact(Skip = "F# generic case pending a decision.")>]\n'
            "let ``disabled generic case`` () = ()\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("F# generic case pending a decision.", reasons)

    def test_an_fsharp_character_literal_is_still_skipped_over(self):
        (self.fixture.tests_dir / "CharTests.fs").write_text(
            "let separator = ';'\n"
            "let newline = '\\n'\n"
            '[<Fact(Skip = "F# char case pending a decision.")>]\n'
            "let ``disabled char case`` () = ()\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("F# char case pending a decision.", reasons)

    def test_finds_a_real_fsharp_skip_outside_a_block_comment(self):
        # The comment fix must not blind the scan to genuine F# skips.
        (self.fixture.tests_dir / "GatedTests.fs").write_text(
            "(* documentation *)\n"
            '[<Fact(Skip = "F# case pending a decision.")>]\n'
            "let ``disabled case`` () = ()\n",
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("F# case pending a decision.", reasons)

    def test_does_not_truncate_a_concatenation_at_an_interpolated_segment(self):
        # Recording only the leading literal would register a reason the runner never reports.
        (self.fixture.tests_dir / "MixedTests.cs").write_text(
            'public sealed class GatedFact : FactAttribute { public GatedFact() { Skip = "first part. " + $"set {Variable} to run."; } }\n',
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn('"first part. " + $"set {Variable} to run."', reasons)

    def test_reason_containing_a_semicolon_is_not_cut_short(self):
        (self.fixture.tests_dir / "SemicolonTests.cs").write_text(
            '[Fact(Skip = "blocked; pending review")]\npublic void Held() {}\n',
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("blocked; pending review", reasons)

    def test_attribute_skip_alongside_another_named_argument(self):
        (self.fixture.tests_dir / "MultiArgTests.cs").write_text(
            '[Fact(Skip = "held for review", DisplayName = "example")]\npublic void M() {}\n',
            encoding="utf-8",
        )

        reasons = {site.reason for site in self.fixture.sites()}

        self.assertIn("held for review", reasons)

    def test_ignores_a_skip_written_in_a_line_comment(self):
        # Broadening discovery to raw text would inventory documentation as a real skip and
        # fail the gate until somebody added a bogus register entry.
        (self.fixture.tests_dir / "DocumentedTests.cs").write_text(
            'public sealed class T {\n    // Example: Skip = "not a real skip";\n    void M() {}\n}\n',
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_a_skip_written_in_a_block_comment(self):
        (self.fixture.tests_dir / "BlockDocumentedTests.cs").write_text(
            'public sealed class T {\n    /* Skip = "not a real skip"; */\n    void M() {}\n}\n',
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_a_skip_inside_a_string_literal(self):
        # Parser and source-generator tests embed C# as fixture text.
        (self.fixture.tests_dir / "FixtureTests.cs").write_text(
            'public sealed class T {\n    const string Source = "[Fact(Skip = \\"inside a fixture\\")]";\n}\n',
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_a_skip_inside_a_verbatim_string_literal(self):
        (self.fixture.tests_dir / "VerbatimFixtureTests.cs").write_text(
            'public sealed class T {\n    const string Source = @"Skip = ""inside a verbatim fixture"";";\n}\n',
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_a_skip_inside_a_raw_string_literal(self):
        # A raw string holding quoted JSON let the scan resume inside the literal, so the
        # fixture text was inventoried as a real skipped test.
        (self.fixture.tests_dir / "RawFixtureTests.cs").write_text(
            'public sealed class T {\n'
            '    const string Payload = """\n'
            '        {"source":"Skip = reason","note":"not a real skip"}\n'
            '        """;\n'
            "}\n",
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_a_skip_inside_a_multi_quote_raw_string(self):
        (self.fixture.tests_dir / "WideRawTests.cs").write_text(
            'public sealed class T {\n'
            '    const string Payload = """"\n'
            '        A raw string containing """ and Skip = "still not real";\n'
            '        """";\n'
            "}\n",
            encoding="utf-8",
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_does_not_match_an_identifier_ending_in_skip(self):
        (self.fixture.tests_dir / "IdentifierTests.cs").write_text(
            "public sealed class T { void M() { var shouldSkip = true; } }\n", encoding="utf-8"
        )

        self.assertEqual(len(self.fixture.sites()), 1)

    def test_ignores_build_output(self):
        obj_dir = self.fixture.tests_dir / "obj" / "Release"
        obj_dir.mkdir(parents=True)
        (obj_dir / "Generated.cs").write_text(QUARANTINED_SOURCE, encoding="utf-8")

        self.assertEqual(len(self.fixture.sites()), 1)


class EvaluateTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = SkipRegisterFixture(Path(self._tmp.name))

    def test_registered_skip_produces_no_problems(self):
        self.assertEqual(self.fixture.problems(), [])

    def test_unregistered_skip_fails(self):
        self.fixture.write_register([])

        problems = self.fixture.problems()

        self.assertTrue(any("not in the register" in p for p in problems), msg=problems)

    def test_stale_register_entry_fails(self):
        self.fixture.write_register(
            [
                entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON),
                entry("tests/Meridian.Tests/Removed.cs", "A skip that no longer exists."),
            ]
        )

        problems = self.fixture.problems()

        self.assertTrue(any("matches no skip in the source" in p for p in problems), msg=problems)

    def test_changed_reason_fails_until_rereviewed(self):
        # Editing the reason in source must not silently keep the old approval.
        (self.fixture.tests_dir / "ExampleTests.cs").write_text(
            QUARANTINED_SOURCE.replace("evidence-capture path", "reporting path"), encoding="utf-8"
        )

        problems = self.fixture.problems()

        self.assertTrue(any("not in the register" in p for p in problems), msg=problems)
        self.assertTrue(any("matches no skip in the source" in p for p in problems), msg=problems)

    def test_expired_review_date_fails(self):
        self.fixture.write_register(
            [entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON, review_by="2026-07-31")]
        )

        problems = self.fixture.problems()

        self.assertTrue(any("due for review" in p for p in problems), msg=problems)

    def test_review_date_today_is_still_within_window(self):
        self.fixture.write_register(
            [entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON, review_by="2026-08-01")]
        )

        self.assertEqual(self.fixture.problems(), [])

    def test_missing_required_field_fails(self):
        incomplete = entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON)
        del incomplete["owner"]
        self.fixture.write_register([incomplete])

        problems = self.fixture.problems()

        self.assertTrue(any("missing required field" in p and "owner" in p for p in problems), msg=problems)

    def test_unknown_category_fails(self):
        self.fixture.write_register(
            [entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON, category="temporary")]
        )

        problems = self.fixture.problems()

        self.assertTrue(any("category 'temporary'" in p for p in problems), msg=problems)

    def test_malformed_review_date_fails(self):
        self.fixture.write_register(
            [entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON, review_by="soon")]
        )

        problems = self.fixture.problems()

        self.assertTrue(any("is not an ISO date" in p for p in problems), msg=problems)

    def test_two_skips_sharing_a_reason_are_registered_independently(self):
        # Keyed on (path, reason) these were indistinguishable, so the gate had to reject the
        # ambiguity. The test identity separates them, and each now carries its own owner and
        # review date — which is the point: deleting one and reusing its reason elsewhere no
        # longer inherits the other's approval.
        second_skip = (
            f'    [Fact(Skip = "{SkipRegisterFixture.QUARANTINE_REASON}")]\n'
            "    public void Disabled_Other()\n"
            "    {\n"
            "    }\n"
        )
        (self.fixture.tests_dir / "ExampleTests.cs").write_text(
            QUARANTINED_SOURCE.replace("}\n", "}\n\n" + second_skip, 1),
            encoding="utf-8",
        )
        self.fixture.write_register(
            [
                entry(SkipRegisterFixture.QUARANTINE_PATH, SkipRegisterFixture.QUARANTINE_REASON),
                entry(
                    SkipRegisterFixture.QUARANTINE_PATH,
                    SkipRegisterFixture.QUARANTINE_REASON,
                    test="Disabled_Other",
                ),
            ]
        )

        self.assertEqual(self.fixture.problems(), [])

    def test_reusing_a_reason_for_a_different_test_needs_its_own_entry(self):
        # The transfer this key exists to prevent: the registered test is gone and a different
        # one now carries the same reason. On (path, reason) the gate saw no change at all.
        (self.fixture.tests_dir / "ExampleTests.cs").write_text(
            QUARANTINED_SOURCE.replace("Disabled_Example", "Disabled_Successor"), encoding="utf-8"
        )

        problems = self.fixture.problems()

        self.assertTrue(any("not in the register" in p for p in problems), msg=problems)
        self.assertTrue(any("matches no skip in the source" in p for p in problems), msg=problems)

    def test_evidence_carries_owner_and_category(self):
        entries = MODULE.load_register(self.fixture.register_path)

        evidence = MODULE.build_evidence(self.fixture.sites(), entries)

        self.assertEqual(evidence["skip_count"], 1)
        self.assertEqual(evidence["by_category"]["quarantined"], 1)
        self.assertEqual(evidence["skips"][0]["owner"], "Storage")

    def test_unregistered_skip_is_labelled_in_evidence(self):
        self.fixture.write_register([])
        entries = MODULE.load_register(self.fixture.register_path)

        evidence = MODULE.build_evidence(self.fixture.sites(), entries)

        self.assertEqual(evidence["by_category"]["unregistered"], 1)


class RepositoryRegisterTests(unittest.TestCase):
    """The real repository's skips must all be owned, not just the synthetic fixture's."""

    def test_repository_skip_register_is_sound(self):
        entries = MODULE.load_register(MODULE.DEFAULT_REGISTER)
        sites = MODULE.discover_skips(REPO_ROOT / "tests", REPO_ROOT)

        # Use each entry's own review window rather than the wall clock so this suite does not
        # start failing on a date change; expiry itself is covered by EvaluateTests.
        problems = MODULE.evaluate(sites, entries, dt.date(1970, 1, 1))

        self.assertEqual(problems, [])
        self.assertGreater(len(sites), 0)


if __name__ == "__main__":
    unittest.main()
