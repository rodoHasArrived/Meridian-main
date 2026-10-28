import json, sys
from pathlib import Path
root = Path(sys.argv[1])
sentence = sys.argv[2]
originals = json.loads((root / 'artifacts/originals.json').read_text())
begin, end = '<!-- shared-context:begin', '<!-- shared-context:end -->'
shared = []
for name, original in originals.items():
    current = (root / name).read_text()
    a, b = original.index(begin), original.index(end) + len(end)
    x, y = current.index(begin), current.index(end) + len(end)
    assert current[:x] == original[:a] and current[y:] == original[b:], name + ': host section changed'
    assert current.count(sentence) == 1, name + ': requested sentence missing/duplicated'
    assert '## Useful Commands\n\n' + sentence + '\n' in current, name + ': wrong insertion'
    assert current == original.replace('## Useful Commands\n', '## Useful Commands\n\n' + sentence + '\n', 1), name + ': unrelated edits'
    shared.append(current[x:y])
assert len(set(shared)) == 1, 'shared blocks differ'
print('PASS: requested guidance synchronized; host-specific text preserved')
