import json
import tempfile
import unittest
from pathlib import Path

from build.scripts.ai import meridian_context_exporter as exporter


class MeridianContextExporterTests(unittest.TestCase):
    def test_payload_digest_changes_when_source_changes(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/architecture").mkdir(parents=True)
            (root / "docs/domain").mkdir(parents=True)
            (root / "docs/ai/context").mkdir(parents=True)
            (root / "docs/adr").mkdir(parents=True)
            (root / "src/Meridian").mkdir(parents=True)
            (root / "tests/Meridian.Tests").mkdir(parents=True)

            vision = root / "docs/architecture/meridian-vision.md"
            vision.write_text("# Meridian Vision\n\nInitial\n", encoding="utf-8")
            (root / "docs/domain/security.md").write_text("# Security\n", encoding="utf-8")
            (root / "docs/ai/context/accounting-context.md").write_text("# Accounting Context\n", encoding="utf-8")
            (root / "docs/adr/017-test.md").write_text("# ADR Test\n", encoding="utf-8")
            (root / "src/Meridian/Meridian.csproj").write_text("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", encoding="utf-8")
            (root / "tests/Meridian.Tests/Meridian.Tests.csproj").write_text("<Project />", encoding="utf-8")

            first = exporter.build_payload(root)
            vision.write_text("# Meridian Vision\n\nChanged\n", encoding="utf-8")
            second = exporter.build_payload(root)

            self.assertNotEqual(first["sourceDigest"], second["sourceDigest"])
            self.assertEqual(second["counts"]["constitutionDocs"], 1)
            self.assertEqual(second["counts"]["domainDocs"], 1)
            self.assertEqual(second["counts"]["contextPacks"], 1)

    def test_check_uses_source_digest_not_timestamp(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            (root / "docs/architecture").mkdir(parents=True)
            (root / "docs/domain").mkdir(parents=True)
            (root / "docs/ai/context").mkdir(parents=True)
            (root / "docs/adr").mkdir(parents=True)
            (root / "src/Meridian").mkdir(parents=True)
            (root / "tests").mkdir(parents=True)
            (root / "docs/architecture/meridian-vision.md").write_text("# Meridian Vision\n", encoding="utf-8")

            payload = exporter.build_payload(root)
            existing = dict(payload)
            existing["generatedAtUtc"] = "2000-01-01T00:00:00Z"
            output = root / "docs/ai/exports/context.json"
            output.parent.mkdir(parents=True)
            output.write_text(json.dumps(existing), encoding="utf-8")

            loaded = exporter._load_existing_json(output)
            self.assertEqual(loaded["sourceDigest"], exporter.build_payload(root)["sourceDigest"])


if __name__ == "__main__":
    unittest.main()
