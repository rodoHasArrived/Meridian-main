"""Release integrity tests: actual assets, tampering, exclusions and ZIP safety."""
import copy
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("export_release", ROOT / "scripts/export_release.py")
EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORT)


class ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = EXPORT.payload(ROOT)
        cls.release = EXPORT.validate(cls.data)

    def test_actual_release_metadata_and_assets_agree(self):
        self.assertEqual("4.3.0", self.release["version"])
        self.assertGreaterEqual(self.release["counts"]["exports"], 351)
        self.assertGreaterEqual(self.release["counts"]["templates"], 30)

    def test_source_edit_requires_a_rebuild(self):
        data = dict(self.data)
        path = next(iter(self.release["build"]["sourceHashes"]))
        data[path] += b"\n// Unbuilt source change\n"
        with self.assertRaisesRegex(ValueError, "Source changed after build"):
            EXPORT.validate(data)

    def test_asset_tampering_is_rejected(self):
        data = dict(self.data)
        data["_preview_runtime.js"] += b"\n// tampered\n"
        with self.assertRaisesRegex(ValueError, "Generated asset changed after build"):
            EXPORT.validate(data)

    def test_new_component_source_requires_a_rebuild(self):
        data = dict(self.data)
        data["components/core/Unbuilt.jsx"] = b"export const Unbuilt = () => null;"
        with self.assertRaisesRegex(ValueError, "source inventory changed after build"):
            EXPORT.validate(data)

    def test_preview_script_styles_and_license_are_required(self):
        for path in ["preview/release-readiness.js", "preview/release-readiness.css", "LICENSE"]:
            with self.subTest(path=path):
                data = dict(self.data)
                del data[path]
                with self.assertRaisesRegex(ValueError, "Required distribution file missing"):
                    EXPORT.validate(data)

    def test_family_metadata_must_cover_exact_public_contract(self):
        data = dict(self.data)
        families = json.loads(data["api/families.json"])
        families["exports"].pop()
        data["api/families.json"] = json.dumps(families).encode()
        with self.assertRaisesRegex(ValueError, "API family membership differs"):
            EXPORT.validate(data)

    def test_self_hosted_fonts_and_licenses_are_required(self):
        for path in ["assets/fonts/IBMPlexSans-Regular.ttf", "assets/fonts/OFL-IBMPlexSans.txt", "assets/fonts/OFL-IBMPlexMono.txt"]:
            with self.subTest(path=path):
                data = dict(self.data)
                del data[path]
                with self.assertRaisesRegex(ValueError, "Required distribution file missing"):
                    EXPORT.validate(data)

    def test_version_mismatch_is_rejected(self):
        data = dict(self.data)
        release = copy.deepcopy(self.release)
        release["version"] = "4.2.0"
        data["release-build.json"] = json.dumps(release).encode()
        with self.assertRaisesRegex(ValueError, "build version or namespace mismatch"):
            EXPORT.validate(data)

    def test_export_excludes_local_and_unreviewed_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for path in ["README.md", "uploads/customer.csv", "scraps/notes.md", "screenshots/client.png", "src/private.js", "node_modules/package/index.js", "unknown.txt"]:
                target = root / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text("synthetic fixture")
            self.assertEqual({"README.md"}, set(EXPORT.payload(root)))

    def test_export_rejects_secrets_and_symlinks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "scripts").mkdir()
            (root / "scripts/leak.js").write_text("-----BEGIN " + "PRIVATE KEY-----")
            with self.assertRaisesRegex(ValueError, "Potential credential"):
                EXPORT.payload(root)
            (root / "scripts/leak.js").unlink()
            (root / "scripts/link.js").symlink_to("/outside")
            with self.assertRaisesRegex(ValueError, "symbolic link"):
                EXPORT.payload(root)

    def test_archive_is_deterministic_and_checksums_cover_payload(self):
        one = EXPORT.archive(self.data, self.release)
        self.assertEqual(one, EXPORT.archive(dict(reversed(list(self.data.items()))), self.release))
        prefix = f"meridian-design-system-{self.release['version']}/"
        with zipfile.ZipFile(io.BytesIO(one)) as archive:
            names = archive.namelist()
            self.assertTrue(all(EXPORT.safe_member(name) and name.startswith(prefix) for name in names))
            sums = archive.read(prefix + "SHA256SUMS").decode().splitlines()
            self.assertEqual(len(names) - 1, len(sums))
            for line in sums:
                digest, path = line.split("  ", 1)
                self.assertEqual(digest, EXPORT.sha256(archive.read(prefix + path)))
            inventory = json.loads(archive.read(prefix + "export-inventory.json"))
            self.assertEqual(len(self.data), len(inventory["files"]))

    def test_archive_path_traversal_is_rejected(self):
        for path in ["../escape", "/absolute", "C:/drive", "a/../../escape", "a\\escape", "a/./b", "a//b", "nul\x00name"]:
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, "Unsafe archive member"):
                EXPORT.archive({path: b"fixture"}, self.release)


if __name__ == "__main__":
    unittest.main()
