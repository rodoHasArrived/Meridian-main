"""Compare a planning answer with the separately captured independent exact-set oracle."""
import json
import hashlib
from pathlib import Path
import sys

root = Path(sys.argv[1])
task = json.loads((root / "task.json").read_text())
protocol = json.loads(Path(__file__).with_name("protocol.json").read_text())
pair = root.name.rsplit("-", 1)[0]
assert task == next(item for item in protocol["tasks"] if item["pair"] == pair), "Task input changed"
for relative, digest in protocol["manifestHashes"].items():
    assert hashlib.sha256((root / relative).read_bytes()).hexdigest() == digest, "Manifest input changed"
expected = json.loads(Path(__file__).with_name("expected.json").read_text())[pair]
actual = json.loads((root / "answer.json").read_text())
assert actual == expected, "Planning answer differs from expected dependency/consumer set"
assert type(actual["browser"]) is bool and type(actual["wpf"]) is bool
print("PASS: exact affected-test set and both consumer flags verified")
