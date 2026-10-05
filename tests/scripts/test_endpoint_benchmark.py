"""The endpoint benchmark must prove overlap and identical, passing test sets."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET


SCRIPTS = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci"
sys.path.insert(0, str(SCRIPTS))
SPEC = importlib.util.spec_from_file_location("endpoint_benchmark", SCRIPTS / "benchmark-endpoints.py")
benchmark = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(benchmark)


class EndpointBenchmarkTests(unittest.TestCase):
    def make_trx(self, directory: Path, concurrent: bool, omit_last: bool = False) -> Path:
        root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        ET.SubElement(root, "Times", start="2026-10-05T12:00:00Z", finish="2026-10-05T12:00:20Z")
        definitions = ET.SubElement(root, "TestDefinitions")
        results = ET.SubElement(root, "Results")
        classes = benchmark.CLASSES[:-1] if omit_last else benchmark.CLASSES
        for index, name in enumerate(classes):
            test = ET.SubElement(definitions, "UnitTest", id=str(index))
            ET.SubElement(test, "TestMethod", className=benchmark.NAMESPACE + name)
            start = index if concurrent else index * 3
            ET.SubElement(
                results, "UnitTestResult", testId=str(index),
                startTime=f"2026-10-05T12:00:{start:02d}Z",
                endTime=f"2026-10-05T12:00:{start + 2:02d}Z",
            )
        path = directory / "endpoint.trx"
        ET.ElementTree(root).write(path, encoding="utf-8")
        return path

    def test_serial_class_timestamps_do_not_count_as_concurrency(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = benchmark.timing_evidence(self.make_trx(Path(directory), concurrent=False))
        self.assertEqual(1, evidence["overlappingClasses"])
        self.assertEqual(20, evidence["testSeconds"])

    def test_concurrent_class_timestamps_prove_overlap(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = benchmark.timing_evidence(self.make_trx(Path(directory), concurrent=True))
        self.assertEqual(2, evidence["overlappingClasses"])

    def test_missing_selected_class_invalidates_benchmark(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "Unexpected benchmark classes"):
                benchmark.timing_evidence(self.make_trx(Path(directory), concurrent=True, omit_last=True))

    def test_skipped_or_changed_tests_invalidate_benchmark(self):
        sample = {
            "counts": {"passed": 10, "failed": 0, "skipped": 0, "other": 0},
            "testIdentityDigest": "unchanged",
        }
        self.assertEqual("unchanged", benchmark.validate_sample(sample, None))
        with self.assertRaisesRegex(ValueError, "identities differ"):
            benchmark.validate_sample(sample, "changed")
        sample["counts"]["skipped"] = 1
        with self.assertRaisesRegex(ValueError, "every selected test"):
            benchmark.validate_sample(sample, "unchanged")


if __name__ == "__main__":
    unittest.main()
