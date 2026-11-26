import importlib.util
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "update_coverage_report.py"
COVERLET_SETTINGS_PATH = REPO_ROOT / "tests" / "coverlet.runsettings"


def _load_module():
    spec = importlib.util.spec_from_file_location("update_coverage_report", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def _cobertura(classes: dict[str, dict[int, int]]) -> str:
    rows = []
    for filename, lines in classes.items():
        body = "".join(f'<line number="{number}" hits="{hits}" />' for number, hits in lines.items())
        rows.append(f'<class name="C" filename="{filename}"><lines>{body}</lines></class>')
    return (
        '<?xml version="1.0"?><coverage line-rate="0"><packages><package name="P"><classes>'
        + "".join(rows)
        + "</classes></package></packages></coverage>"
    )


class CoverageAggregateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = _load_module()
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def _write(self, name: str, classes: dict[str, dict[int, int]]) -> Path:
        path = self.root / name / "coverage.cobertura.xml"
        path.parent.mkdir(parents=True)
        path.write_text(_cobertura(classes), encoding="utf-8")
        return path

    def test_a_line_is_covered_when_any_project_executed_it(self) -> None:
        first = self._write("a", {"src/Ledger.cs": {1: 1, 2: 0, 3: 0, 4: 0}})
        second = self._write("b", {"src/Ledger.cs": {1: 0, 2: 3, 3: 0, 4: 0}})

        rate, line_count = self.module._aggregate_coverage([first, second])

        self.assertEqual(line_count, 4)
        self.assertAlmostEqual(rate, 0.5)

    def test_files_weigh_by_their_lines_not_equally(self) -> None:
        large = self._write("large", {"src/Large.cs": {number: 0 for number in range(1, 100)}})
        small = self._write("small", {"src/Small.cs": {1: 1}})

        rate, line_count = self.module._aggregate_coverage([large, small])

        # A per-file average would report 50%.
        self.assertEqual(line_count, 100)
        self.assertAlmostEqual(rate, 0.01)

    def test_unreadable_files_do_not_produce_a_reading(self) -> None:
        broken = self.root / "coverage.cobertura.xml"
        broken.write_text("<coverage", encoding="utf-8")

        self.assertIsNone(self.module._aggregate_coverage([broken]))
        self.assertIsNone(self.module._aggregate_coverage([]))


class CoverletFilterTests(unittest.TestCase):
    def test_every_meridian_assembly_is_instrumented(self) -> None:
        settings = ET.parse(COVERLET_SETTINGS_PATH).getroot()
        include = settings.findtext(".//Configuration/Include")
        exclude = settings.findtext(".//Configuration/Exclude")

        # Coverlet compares the bracketed filter with the whole assembly name:
        # "[Meridian]*" selects only Meridian.dll, not Meridian.Application.dll.
        self.assertEqual(include, "[Meridian*]*")
        for owner in ("[*Tests]*", "[Meridian.TestSupport]*", "[Meridian.ProcessTestHelper]*"):
            self.assertIn(owner, exclude.split(","))


if __name__ == "__main__":
    unittest.main()
