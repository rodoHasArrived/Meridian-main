import json
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

from PIL import Image


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "scripts" / "dev" / "validate-screenshot-captures.py"


class ValidateScreenshotCapturesTests(unittest.TestCase):
    def run_validator(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
        )

    def write_png(self, path: Path, *, flat: bool = False) -> None:
        image = Image.new("RGB", (640, 420), (255, 255, 255))
        if not flat:
            for x in range(640):
                for y in range(420):
                    image.putpixel(
                        (x, y),
                        (
                            (x * 3 + y) % 256,
                            (x + y * 2) % 256,
                            (x * 5 + y * 7) % 256,
                        ),
                    )
        image.save(path)

    def test_web_validation_passes_for_fresh_route_matched_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "web-a.png")
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "passed",
                        "captures": [
                            {
                                "name": "web-a",
                                "route": "/strategy",
                                "path": str(output / "web-a.png"),
                                "url": "http://127.0.0.1:5173/workstation/strategy",
                                "expectedPath": "/workstation/strategy",
                                "actualPath": "/workstation/strategy",
                                "status": "passed",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
                "--require-fresh",
            )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Screenshot capture validation passed", result.stdout)

    def test_web_validation_fails_wrong_route_and_blank_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "web-a.png", flat=True)
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "passed",
                        "captures": [
                            {
                                "name": "web-a",
                                "route": "/portfolio",
                                "path": str(output / "web-a.png"),
                                "url": "http://127.0.0.1:5173/workstation/strategy",
                                "expectedPath": "/workstation/strategy",
                                "actualPath": "/workstation/portfolio",
                                "status": "passed",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
                "--min-bytes",
                "100",
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("captured route '/portfolio', expected '/strategy'", result.stderr)
        self.assertIn("actual browser path '/workstation/portfolio', expected '/workstation/strategy'", result.stderr)
        self.assertIn("capture is likely blank", result.stderr)
        self.assertIn("capture is likely low-entropy", result.stderr)

    def test_web_validation_fails_stale_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            capture_path = output / "web-a.png"
            self.write_png(capture_path)
            stale_time = datetime.now(timezone.utc) - timedelta(hours=4)
            os.utime(capture_path, (stale_time.timestamp(), stale_time.timestamp()))
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "passed",
                        "captures": [
                            {
                                "name": "web-a",
                                "route": "/strategy",
                                "path": str(capture_path),
                                "url": "http://127.0.0.1:5173/workstation/strategy",
                                "expectedPath": "/workstation/strategy",
                                "actualPath": "/workstation/strategy",
                                "status": "passed",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
                "--require-fresh",
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("web-a.png is stale", result.stderr)

    def test_web_validation_fails_when_manifest_omits_expected_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "web-a.png")
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "passed",
                        "captures": [],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Manifest is missing expected web capture entries: web-a.", result.stderr)

    def test_web_validation_fails_when_manifest_path_does_not_match_output_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "web-a.png")
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "passed",
                        "captures": [
                            {
                                "name": "web-a",
                                "route": "/strategy",
                                "path": str(root / "other-run" / "web-a.png"),
                                "url": "http://127.0.0.1:5173/workstation/strategy",
                                "expectedPath": "/workstation/strategy",
                                "actualPath": "/workstation/strategy",
                                "status": "passed",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("web-a manifest capture path does not match the requested output file", result.stderr)

    def test_web_validation_fails_when_manifest_status_failed(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "web-a.png")
            config = root / "routes.json"
            config.write_text(
                json.dumps({"captures": [{"name": "web-a", "path": "/strategy"}]}),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "status": "failed",
                        "captures": [
                            {
                                "name": "web-a",
                                "route": "/strategy",
                                "path": str(output / "web-a.png"),
                                "url": "http://127.0.0.1:5173/workstation/strategy",
                                "expectedPath": "/workstation/strategy",
                                "actualPath": "/workstation/strategy",
                                "status": "passed",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "web",
                "--output-dir",
                str(output),
                "--web-routes",
                str(config),
                "--manifest",
                str(manifest),
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Manifest status is 'failed', expected 'passed'.", result.stderr)

    def test_desktop_validation_fails_wrong_page_tag(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "wpf-a.png")
            workflows = root / "desktop-workflows.json"
            workflows.write_text(
                json.dumps(
                    {
                        "workflows": [
                            {
                                "name": "screenshot-catalog",
                                "steps": [{"captureName": "wpf-a", "pageTag": "ExpectedPage"}],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "run": {"startedAt": datetime.now(timezone.utc).isoformat()},
                        "steps": [
                            {
                                "capturePath": str(output / "wpf-a.png"),
                                "observedPageTag": "OtherPage",
                                "status": "ok",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "desktop",
                "--output-dir",
                str(output),
                "--desktop-workflows",
                str(workflows),
                "--manifest",
                str(manifest),
                "--require-fresh",
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("observed page tag 'OtherPage', expected 'ExpectedPage'", result.stderr)

    def test_desktop_validation_fails_when_manifest_omits_expected_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "wpf-a.png")
            workflows = root / "desktop-workflows.json"
            workflows.write_text(
                json.dumps(
                    {
                        "workflows": [
                            {
                                "name": "screenshot-catalog",
                                "steps": [{"captureName": "wpf-a", "pageTag": "ExpectedPage"}],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "run": {"startedAt": datetime.now(timezone.utc).isoformat()},
                        "steps": [],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "desktop",
                "--output-dir",
                str(output),
                "--desktop-workflows",
                str(workflows),
                "--manifest",
                str(manifest),
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Manifest is missing expected desktop capture entries: wpf-a.", result.stderr)

    def test_desktop_validation_fails_when_manifest_path_does_not_match_output_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output = root / "screens"
            output.mkdir()
            self.write_png(output / "wpf-a.png")
            workflows = root / "desktop-workflows.json"
            workflows.write_text(
                json.dumps(
                    {
                        "workflows": [
                            {
                                "name": "screenshot-catalog",
                                "steps": [{"captureName": "wpf-a", "pageTag": "ExpectedPage"}],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            manifest = root / "manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "run": {"startedAt": datetime.now(timezone.utc).isoformat()},
                        "steps": [
                            {
                                "capturePath": str(root / "other-run" / "wpf-a.png"),
                                "observedPageTag": "ExpectedPage",
                                "status": "ok",
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = self.run_validator(
                "--surface",
                "desktop",
                "--output-dir",
                str(output),
                "--desktop-workflows",
                str(workflows),
                "--manifest",
                str(manifest),
            )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("wpf-a manifest capture path does not match the requested output file", result.stderr)


if __name__ == "__main__":
    unittest.main()
