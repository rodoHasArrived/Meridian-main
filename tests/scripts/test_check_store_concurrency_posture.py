from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[2]
    / "build"
    / "scripts"
    / "ci"
    / "check-store-concurrency-posture.py"
)
SPEC = importlib.util.spec_from_file_location("check_store_concurrency_posture", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_store_concurrency_posture"] = guard
SPEC.loader.exec_module(guard)

REPO_ROOT = Path(__file__).resolve().parents[2]


def classify(files: dict[str, str]) -> dict[str, str]:
    """Run the classifier over a throwaway src/ tree holding exactly these files."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir) / "src"
        for rel, content in files.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        return guard.classify(root)


class CheckStoreConcurrencyPostureTests(unittest.TestCase):
    def test_cross_process_lease_is_recognised(self) -> None:
        found = classify(
            {
                "FileThingStore.cs": """
public sealed class FileThingStore : IThingStore
{
    private async Task SaveAsync() => await AcquireMutationLeaseAsync();
}
"""
            }
        )

        self.assertEqual(found, {"FileThingStore": "cross-process-lease"})

    def test_in_process_serialization_is_recognised(self) -> None:
        found = classify(
            {
                "FileThingStore.cs": """
public sealed class FileThingStore : IThingStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
}
"""
            }
        )

        self.assertEqual(found, {"FileThingStore": "in-process-serialized"})

    def test_a_stated_posture_is_recognised(self) -> None:
        found = classify(
            {
                "FileThingStore.cs": """
/// <summary>
/// <b>Concurrency posture: append-only.</b> Never rewrites an existing line.
/// </summary>
public sealed class FileThingStore : IThingStore
{
}
"""
            }
        )

        self.assertEqual(found, {"FileThingStore": "declared"})

    def test_a_store_with_no_posture_is_unclassified(self) -> None:
        found = classify(
            {
                "FileThingStore.cs": """
public sealed class FileThingStore : IThingStore
{
    public Task SaveAsync(Thing t) => File.WriteAllTextAsync(_path, Serialize(t));
}
"""
            }
        )

        self.assertEqual(found, {"FileThingStore": "unclassified"})

    def test_posture_is_inherited_from_a_base_class(self) -> None:
        """The shape that made a per-file scan wrong: most stores in this repo derive from
        JsonFileSnapshotStore, whose gate and atomic write live in the base file, not theirs."""
        found = classify(
            {
                "JsonFileSnapshotStore.cs": """
public abstract class JsonFileSnapshotStore<TSnapshot>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
}
""",
                "FileThingStore.cs": """
public sealed class FileThingStore : JsonFileSnapshotStore<ThingSnapshot>, IThingStore
{
}
""",
            }
        )

        self.assertEqual(found["FileThingStore"], "in-process-serialized")

    def test_inheritance_cycles_do_not_recurse_forever(self) -> None:
        found = classify(
            {
                "A.cs": "public class FileAStore : FileBStore { }",
                "B.cs": "public class FileBStore : FileAStore { }",
            }
        )

        self.assertEqual(found, {"FileAStore": "unclassified", "FileBStore": "unclassified"})

    def test_only_file_backed_store_types_are_in_scope(self) -> None:
        found = classify(
            {
                "Things.cs": """
public sealed class ThingService { }
public sealed class PostgresThingStore { }
public sealed class FileThingStore { }
public sealed class JsonlThingRepository { }
"""
            }
        )

        self.assertEqual(sorted(found), ["FileThingStore", "JsonlThingRepository"])

    def test_repository_tree_has_every_store_classified(self) -> None:
        """The live invariant: no file-backed store in this repo lacks a posture."""
        postures = guard.classify(REPO_ROOT / "src")
        unclassified = sorted(n for n, p in postures.items() if p == "unclassified")

        self.assertEqual(unclassified, [])
        self.assertGreater(len(postures), 40, "classifier stopped finding the store surface")


if __name__ == "__main__":
    unittest.main()
