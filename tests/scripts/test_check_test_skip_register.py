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
    owner: str = "Storage",
    category: str = "quarantined",
    tracking: str = "PRD-112",
    review_by: str = "2027-01-01",
) -> dict:
    return {
        "path": path,
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

    def test_duplicate_reasons_in_one_file_fail(self):
        # Two skips sharing a reason cannot be registered or reviewed independently, so the
        # gate rejects the ambiguity rather than silently approving both from one entry.
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

        problems = self.fixture.problems()

        self.assertTrue(any("duplicate skip reason" in p for p in problems), msg=problems)


class EvidenceTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = SkipRegisterFixture(Path(self._tmp.name))

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
