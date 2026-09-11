"""Source documentation hashes must survive checkout platform changes."""

from __future__ import annotations

import hashlib
import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


DOCS_SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DOCS_SCRIPT_DIR))
spec = importlib.util.spec_from_file_location(
    "validate_doc_hashes_under_test", DOCS_SCRIPT_DIR / "validate-doc-hashes.py"
)
assert spec is not None and spec.loader is not None
doc_hashes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(doc_hashes)


class SourceDocHashPortabilityTests(unittest.TestCase):
    def test_source_order_is_ordinal_and_independent_of_checkout_root(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src" / "Sample"
            source.mkdir(parents=True)
            for filename in ("apple.cs", "Zebra.cs"):
                (source / filename).write_bytes(b"class Example {}\n")

            content_hash = hashlib.sha256(b"class Example {}\n").hexdigest()
            expected = hashlib.sha256(
                f"src/Sample/Zebra.cs:{content_hash}\nsrc/Sample/apple.cs:{content_hash}".encode()
            ).hexdigest()
            self.assertEqual(expected, doc_hashes.tree_hash(root, source))

    def test_source_text_hash_survives_lf_to_crlf_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            path = source / "Example.csproj"
            path.write_bytes(b"<Project>\n</Project>\n")
            before = doc_hashes.tree_hash(root, source)
            path.write_bytes(b"<Project>\r\n</Project>\r\n")
            self.assertEqual(before, doc_hashes.tree_hash(root, source))

            path.write_bytes(b"<Project Changed='true'>\r\n</Project>\r\n")
            self.assertNotEqual(before, doc_hashes.tree_hash(root, source))

    def test_readme_normalizes_newlines_but_preserves_documentation_changes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            registry = root / "docs/source/data/source-modules.yml"
            registry.parent.mkdir(parents=True)
            registry.write_text(
                "modules:\n  - id: SRC-SAMPLE\n    path: src/Sample\n"
                "    readme: src/Sample/README.md\n", encoding="utf-8"
            )
            source = root / "src/Sample"
            source.mkdir(parents=True)
            readme = source / "README.md"
            readme.write_bytes(b"# Sample\n\nOwned workflow.\n")
            before = doc_hashes.build_manifest(root)["modules"][0]["readme_hash"]
            readme.write_bytes(b"# Sample\r\n\r\nOwned workflow.\r\n")
            self.assertEqual(before, doc_hashes.build_manifest(root)["modules"][0]["readme_hash"])

            readme.write_bytes(b"# Sample\r\n\r\nChanged workflow.\r\n")
            self.assertNotEqual(before, doc_hashes.build_manifest(root)["modules"][0]["readme_hash"])

    def test_non_utf8_source_bytes_are_not_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            payload = b"\xff\r\n"
            (source / "legacy.json").write_bytes(payload)
            content_hash = hashlib.sha256(payload).hexdigest()
            expected = hashlib.sha256(f"src/legacy.json:{content_hash}".encode()).hexdigest()
            self.assertEqual(expected, doc_hashes.tree_hash(root, source))

    def test_build_and_dependency_outputs_stay_excluded(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "src"
            source.mkdir()
            (source / "Example.cs").write_bytes(b"class Example {}\n")
            before = doc_hashes.tree_hash(root, source)
            for name in ("bin", "obj", "node_modules", "wwwroot"):
                ignored = source / name
                ignored.mkdir()
                (ignored / "generated.cs").write_bytes(b"generated output\n")
            self.assertEqual(before, doc_hashes.tree_hash(root, source))


if __name__ == "__main__":
    unittest.main()
