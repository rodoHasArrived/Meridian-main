import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image


class ScreenshotDiffReportTests(unittest.TestCase):
    def test_classifies_noise_review_and_blocking(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            current_root = root / "current"
            baseline_root = root / "baseline"
            report_dir = root / "report"
            current_root.mkdir(parents=True)
            baseline_root.mkdir(parents=True)

            img_name = "docs/screenshots/desktop/example.png"
            (current_root / "docs/screenshots/desktop").mkdir(parents=True, exist_ok=True)
            (baseline_root / "docs/screenshots/desktop").mkdir(parents=True, exist_ok=True)

            baseline = Image.new("RGB", (20, 20), (0, 0, 0))
            baseline.save(baseline_root / img_name)

            current = Image.new("RGB", (20, 20), (0, 0, 0))
            for x in range(10):
                for y in range(10):
                    current.putpixel((x, y), (255, 255, 255))
            current.save(current_root / img_name)

            changed_files = root / "changed.txt"
            changed_files.write_text(f"{img_name}\n", encoding="utf-8")

            config = root / "config.json"
            config.write_text(
                json.dumps(
                    {
                        "version": 1,
                        "default": {
                            "reviewNeededThreshold": 0.05,
                            "blockingThreshold": 0.3,
                            "pixelChannelTolerance": 1,
                            "masks": [],
                        },
                        "images": {},
                    }
                ),
                encoding="utf-8",
            )

            output_json = root / "summary.json"
            subprocess.run(
                [
                    sys.executable,
                    "scripts/dev/screenshot_diff_report.py",
                    "--current-root",
                    str(current_root),
                    "--baseline-root",
                    str(baseline_root),
                    "--config",
                    str(config),
                    "--report-dir",
                    str(report_dir),
                    "--changed-files",
                    str(changed_files),
                    "--approval",
                    "pending",
                    "--output-json",
                    str(output_json),
                ],
                check=True,
                cwd=Path(__file__).resolve().parents[2],
            )

            payload = json.loads(output_json.read_text(encoding="utf-8"))
            self.assertEqual(1, payload["counts"]["review-needed"])
            self.assertEqual(0, payload["counts"]["blocking-regression"])
            self.assertEqual(0, payload["counts"]["non-blocking-noise"])

    def test_allows_new_screenshot_files_without_baseline(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            current_root = root / "current"
            baseline_root = root / "baseline"
            report_dir = root / "report"
            current_root.mkdir(parents=True)
            baseline_root.mkdir(parents=True)

            img_name = "docs/screenshots/web/web-trading-workspace.png"
            (current_root / "docs/screenshots/web").mkdir(parents=True, exist_ok=True)

            current = Image.new("RGB", (20, 20), (40, 80, 120))
            current.save(current_root / img_name)

            changed_files = root / "changed.txt"
            changed_files.write_text(f"{img_name}\n", encoding="utf-8")

            config = root / "config.json"
            config.write_text(
                json.dumps(
                    {
                        "version": 1,
                        "default": {
                            "reviewNeededThreshold": 0.05,
                            "blockingThreshold": 0.3,
                            "pixelChannelTolerance": 1,
                            "masks": [],
                        },
                        "images": {},
                    }
                ),
                encoding="utf-8",
            )

            output_json = root / "summary.json"
            subprocess.run(
                [
                    sys.executable,
                    "scripts/dev/screenshot_diff_report.py",
                    "--current-root",
                    str(current_root),
                    "--baseline-root",
                    str(baseline_root),
                    "--config",
                    str(config),
                    "--report-dir",
                    str(report_dir),
                    "--changed-files",
                    str(changed_files),
                    "--approval",
                    "pending",
                    "--output-json",
                    str(output_json),
                ],
                check=True,
                cwd=Path(__file__).resolve().parents[2],
            )

            payload = json.loads(output_json.read_text(encoding="utf-8"))
            self.assertEqual(0, payload["counts"]["review-needed"])
            self.assertEqual(0, payload["counts"]["blocking-regression"])
            self.assertEqual(1, payload["counts"]["non-blocking-noise"])
            self.assertEqual("New screenshot file", payload["results"][0]["reason"])


if __name__ == "__main__":
    unittest.main()
