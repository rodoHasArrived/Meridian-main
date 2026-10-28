"""Independent exact-change acceptance check for the preregistered maintenance tasks."""
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
task = json.loads((root / "task.json").read_text())
originals = json.loads((root / "originals.json").read_text())
canonical = ".claude/skills/_shared/project-context.md"
begin, end = "<!-- shared-context:begin", "<!-- shared-context:end -->"
original = originals[canonical]
shared = original[original.index(begin):original.index(end) + len(end)]
for heading, sentence in task["sections"].items():
    marker = "## " + heading + "\n"
    assert shared.count(marker) == 1, "ambiguous acceptance heading"
    shared = shared.replace(marker, marker + "\n" + sentence + "\n", 1)
for name, original in originals.items():
    start, stop = original.index(begin), original.index(end) + len(end)
    expected = original[:start] + shared + original[stop:]
    assert (root / name).read_text() == expected, name + ": requested edits or preservation failed"
print("PASS: all requested edits synchronized; all other text preserved")
