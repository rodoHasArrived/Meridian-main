"""Compare a planning answer with the separately captured independent exact-set oracle."""
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
task = json.loads((root / "task.json").read_text())
expected = json.loads(Path(__file__).with_name("expected.json").read_text())[task["pair"]]
actual = json.loads((root / "answer.json").read_text())
assert actual == expected, "Planning answer differs from expected dependency/consumer set"
assert type(actual["browser"]) is bool and type(actual["wpf"]) is bool
print("PASS: exact affected-test set and both consumer flags verified")
