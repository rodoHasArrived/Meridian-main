import importlib.util
import sys
import tempfile
import unittest
from datetime import date, timedelta
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "docs" / "check-codex-memory.py"

spec = importlib.util.spec_from_file_location("check_codex_memory", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


class CheckCodexMemoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        (self.root / ".codex" / "memory" / "repo").mkdir(parents=True)
        (self.root / ".codex" / "memory" / "archive").mkdir(parents=True)
        (self.root / "docs").mkdir()
        (self.root / "docs" / "source.md").write_text("# Source\n", encoding="utf-8")

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def yaml_for_entry(self, entry: dict) -> str:
        replaced_by = ""
        if "replaced_by" in entry:
            replaced_by = "replaced_by: [" + ", ".join(entry["replaced_by"]) + "]\n"
        retired_because = ""
        if "retired_because" in entry:
            retired_because = f"retired_because: {entry['retired_because']}\n"
        return f"""id: {entry['id']}
tier: {entry['tier']}
scope: {entry['scope']}
file: {entry['file']}
tags: [validation]
load_when:
  skills: []
  paths: []
  intents: []
  branches: []
  tags: []
  task:
    ids: []
    work_modes: []
    intents: []
    paths: []
confidence: {entry['confidence']}
freshness: {entry['freshness']}
source_refs: [{', '.join(entry['source_refs'])}]
review_after: {entry['review_after']}
invalidates_when: [{'; '.join(entry['invalidates_when'])}]
{retired_because}{replaced_by}"""

    def write_memory(self, entry: dict, body: str = "# Memory\n") -> None:
        memory_path = self.root / entry["file"]
        memory_path.parent.mkdir(parents=True, exist_ok=True)
        entry_yaml = self.yaml_for_entry(entry)
        memory_path.write_text(f"---\n{entry_yaml}---\n\n{body}", encoding="utf-8")
        index_text = f"version: 1\nmemory_root: .codex/memory\nentries:\n  - " + entry_yaml.replace("\n", "\n    ").rstrip() + "\n"
        (self.root / ".codex" / "memory" / "index.yml").write_text(index_text, encoding="utf-8")

    def base_entry(self) -> dict:
        return {
            "id": "repo:test",
            "tier": "repo",
            "scope": "repo",
            "file": ".codex/memory/repo/test.md",
            "tags": ["validation"],
            "load_when": {
                "skills": [],
                "paths": [],
                "intents": [],
                "branches": [],
                "tags": [],
                "task": {"ids": [], "work_modes": [], "intents": [], "paths": []},
            },
            "confidence": "high",
            "freshness": "fresh",
            "source_refs": ["docs/source.md"],
            "review_after": (date.today() + timedelta(days=7)).isoformat(),
            "invalidates_when": ["Source contract changes."],
        }

    def findings_for(self, entry: dict) -> list:
        self.write_memory(entry)
        findings, _ = module.collect_findings(self.root)
        return findings

    def test_missing_invalidation_triggers_are_errors(self) -> None:
        entry = self.base_entry()
        entry["invalidates_when"] = []

        findings = self.findings_for(entry)

        self.assertTrue(any(f.severity == "error" and "invalidates_when must contain at least one entry" in f.message for f in findings))

    def test_deleted_source_references_are_warnings(self) -> None:
        entry = self.base_entry()
        entry["source_refs"] = ["docs/deleted.md"]

        findings = self.findings_for(entry)

        self.assertTrue(any(f.severity == "warning" and "source_ref does not exist: docs/deleted.md" in f.message for f in findings))
        self.assertFalse(any(f.severity == "error" and "source_ref does not exist" in f.message for f in findings))

    def test_stale_review_dates_are_warnings_and_route_stale(self) -> None:
        entry = self.base_entry()
        entry["review_after"] = (date.today() - timedelta(days=1)).isoformat()

        findings = self.findings_for(entry)
        selected = module.select_entries([entry], paths=[], tags=[], stale_only=True)

        self.assertTrue(any(f.severity == "warning" and "Memory review date has passed" in f.message for f in findings))
        self.assertEqual([entry], selected)

    def test_archive_entries_without_retirement_rationale_are_errors(self) -> None:
        entry = self.base_entry()
        entry.update({
            "id": "archive:test",
            "tier": "archive",
            "scope": "archive",
            "file": ".codex/memory/archive/test.md",
        })

        findings = self.findings_for(entry)

        self.assertTrue(any(f.severity == "error" and "retired_because" in f.message for f in findings))
        self.assertTrue(any(f.severity == "error" and "replaced_by" in f.message for f in findings))


if __name__ == "__main__":
    unittest.main()
