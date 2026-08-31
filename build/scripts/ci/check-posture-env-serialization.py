#!/usr/bin/env python3
"""Guard: every test class that mutates a deployment-posture environment variable is serialized.

ProductionServiceRegistrationPolicy.IsProductionEnvironment() reads four process-global
variables -- ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT, MERIDIAN_ENVIRONMENT and
MERIDIAN_DEPLOYMENT_ENVIRONMENT -- during any service composition. Environment.SetEnvironmentVariable
mutates the process, so a test that sets one of them is visible to every other test running at the
same moment, however correctly it restores the value afterwards (#2680).

xUnit runs collections declared with DisableParallelization = true apart from the parallel ones,
so the fix is for each mutating class to sit in such a collection. That fix decays silently: the
invariant lives in a doc comment, and the next test class added without the attribute reintroduces
the race with nothing failing at the time it is introduced -- the same shape of drift the other
ratchets in this directory exist to catch.

So this fails CI when a test class mutates a posture variable outside a non-parallel collection.
Pollution is propagated through the two ways a class inherits it without naming the variable
itself: a fixture it consumes via IClassFixture<T>, and a base class it derives from. A class that
only *reads* a posture variable is not a polluter and is not flagged. Fixtures and abstract bases
are not flagged either -- they carry no collection attribute of their own, so the attribute has to
go on the concrete classes, which is where this reports.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
TESTS_ROOT = REPO_ROOT / "tests"

POSTURE_VARIABLES = (
    "ASPNETCORE_ENVIRONMENT",
    "DOTNET_ENVIRONMENT",
    "MERIDIAN_ENVIRONMENT",
    "MERIDIAN_DEPLOYMENT_ENVIRONMENT",
)

# A mutation, not a read: either a direct Set, or one of the repo's scope helpers, which set on
# construction and restore on dispose. Both are process-global for the lifetime of the scope.
MUTATION_PATTERN = re.compile(
    r"(?:SetEnvironmentVariable|EnvironmentVariableScope|EnvironmentScope)"
    r"\(\s*\"(?:" + "|".join(POSTURE_VARIABLES) + r")\"",
)

COLLECTION_ATTRIBUTE_PATTERN = re.compile(
    r"\[Collection\(\s*(?:\"([^\"]+)\"|([A-Za-z0-9_.]+))\s*\)\]",
)
COLLECTION_DEFINITION_PATTERN = re.compile(
    r"\[CollectionDefinition\(\s*(?:\"(?P<literal>[^\"]+)\"|(?P<member>[A-Za-z0-9_.]+))\s*"
    r"(?P<rest>[^\]]*)\)\]",
)
NAME_CONSTANT_PATTERN = re.compile(
    r"const\s+string\s+(?P<field>[A-Za-z0-9_]+)\s*=\s*\"(?P<value>[^\"]+)\"",
)
# Anchored at line start so prose in a doc comment ("/// Per-class test fixture ...") cannot be
# read as a declaration.
CLASS_PATTERN = re.compile(
    r"^[ \t]*(?:public\s+|internal\s+|private\s+|protected\s+)?"
    r"(?P<abstract>abstract\s+)?(?:sealed\s+|static\s+|partial\s+)*class\s+(?P<name>[A-Za-z0-9_]+)"
    r"\s*(?:<[^>{]*>)?\s*(?::\s*(?P<bases>[^{]+))?",
    re.MULTILINE,
)
FIXTURE_PATTERN = re.compile(r"I(?:Class|Collection)Fixture<\s*(?P<name>[A-Za-z0-9_]+)\s*>")

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj", "workers"}


def _iter_test_sources(tests_root: Path) -> list[Path]:
    paths: list[Path] = []
    for current_root, directories, files in os.walk(tests_root, topdown=True, followlinks=False):
        directories[:] = sorted(d for d in directories if d.lower() not in EXCLUDED_DIRECTORY_NAMES)
        paths.extend(Path(current_root) / f for f in files if f.lower().endswith(".cs"))
    return sorted(paths)


def _type_names(bases: str | None) -> set[str]:
    if not bases:
        return set()
    names: set[str] = set()
    depth = 0
    current = ""
    for char in bases:
        if char == "<":
            depth += 1
        elif char == ">":
            depth -= 1
        if char == "," and depth == 0:
            names.add(current.strip())
            current = ""
        else:
            current += char
    names.add(current.strip())
    resolved: set[str] = set()
    for name in names:
        name = name.strip()
        if not name:
            continue
        match = FIXTURE_PATTERN.match(name)
        resolved.add(match.group("name") if match else name.split("<")[0].split(".")[-1].strip())
    return resolved


def find_unserialized_polluters(tests_root: Path) -> dict[str, list[str]]:
    """Maps repo-relative path -> concrete class names that mutate a posture variable unserialized."""
    sources = {p: p.read_text(encoding="utf-8", errors="replace") for p in _iter_test_sources(tests_root)}
    if not sources:
        return {}
    repo_root = tests_root.parent

    constants: dict[str, str] = {}
    for text in sources.values():
        for match in NAME_CONSTANT_PATTERN.finditer(text):
            constants[match.group("field")] = match.group("value")

    non_parallel: set[str] = set()
    for text in sources.values():
        for match in COLLECTION_DEFINITION_PATTERN.finditer(text):
            if "DisableParallelization" not in match.group("rest"):
                continue
            if match.group("literal"):
                non_parallel.add(match.group("literal"))
            else:
                leaf = match.group("member").rsplit(".", 1)[-1]
                non_parallel.add(match.group("member"))
                non_parallel.add(leaf)
                if leaf in constants:
                    non_parallel.add(constants[leaf])

    # class name -> (declaring file, is_abstract, dependency type names, mutates in its own body)
    declarations: dict[str, tuple[Path, bool, set[str], bool]] = {}
    for path, text in sources.items():
        mutates = bool(MUTATION_PATTERN.search(text))
        for match in CLASS_PATTERN.finditer(text):
            name = match.group("name")
            dependencies = _type_names(match.group("bases"))
            dependencies |= {m.group("name") for m in FIXTURE_PATTERN.finditer(text)}
            declarations[name] = (path, bool(match.group("abstract")), dependencies, mutates)

    # A class pollutes if it mutates directly, or depends on something that pollutes. Propagate to
    # a fixed point so class -> base -> fixture chains are covered.
    polluting = {name for name, (_, _, _, mutates) in declarations.items() if mutates}
    changed = True
    while changed:
        changed = False
        for name, (_, _, dependencies, _) in declarations.items():
            if name not in polluting and dependencies & polluting:
                polluting.add(name)
                changed = True

    violations: dict[str, list[str]] = {}
    for path, text in sources.items():
        declared: set[str] = set()
        for match in COLLECTION_ATTRIBUTE_PATTERN.finditer(text):
            literal, member = match.group(1), match.group(2)
            if literal:
                declared.add(literal)
            elif member:
                leaf = member.rsplit(".", 1)[-1]
                declared |= {member, leaf}
                if leaf in constants:
                    declared.add(constants[leaf])
        if declared & non_parallel:
            continue

        flagged = sorted(
            name
            for name, (declaring, is_abstract, _, _) in declarations.items()
            # A fixture or abstract base carries no collection attribute of its own; the concrete
            # classes consuming it are where the attribute goes, and each is checked on its file.
            if declaring == path and not is_abstract and name in polluting
            and not any(f"IClassFixture<{name}>" in other for other in sources.values())
        )
        if flagged:
            violations[path.relative_to(repo_root).as_posix()] = flagged
    return violations


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce posture-environment test serialization.")
    parser.add_argument("--tests-root", default=str(TESTS_ROOT))
    args = parser.parse_args()

    violations = find_unserialized_polluters(Path(args.tests_root))
    if violations:
        print("Posture-environment serialization guard FAILED.", file=sys.stderr)
        print(
            "These test classes mutate a process-global deployment-posture variable (directly, or "
            "through a fixture or base class) while running in a parallel collection, so any test "
            "composing services at the same moment can read the mutated value (#2680):",
            file=sys.stderr,
        )
        for rel, classes in sorted(violations.items()):
            print(f"  {rel}: {', '.join(classes)}", file=sys.stderr)
        print(
            '\nPut each class in a collection declared with DisableParallelization = true '
            '(e.g. [Collection("Sequential")], or the suite\'s own serialized collection).',
            file=sys.stderr,
        )
        return 1

    print("Posture-environment serialization guard: no unserialized posture mutations.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
