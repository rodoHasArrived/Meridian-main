#!/usr/bin/env python3
"""Decide whether a pull request needs CodeQL analysis for one language.

Builds on detect-dotnet-changes.py: recognized documentation never requires analysis, and an
empty diff or any path not positively known to be irrelevant to the language requires it.
Two narrow exclusions are added on top of documentation:

* C# analysis is not required for paths under the browser dashboard source tree, which holds
  no C# that CodeQL extracts.
* JavaScript/TypeScript analysis is not required for .NET source and project files, which hold
  no JavaScript or TypeScript.

Everything else (workflows, CodeQL configuration, scripts, shared JSON, Razor/cshtml, unknown
files) requires both languages. Only pull requests are classified; callers analyze every other
event unconditionally.
"""
from __future__ import annotations

import argparse
import importlib.util
import sys
from pathlib import Path, PurePosixPath

DETECTOR_PATH = Path(__file__).resolve().parent / "detect-dotnet-changes.py"
LANGUAGES = ("csharp", "javascript-typescript")
DASHBOARD_PREFIX = ("src", "Meridian.Ui", "dashboard")
DOTNET_ONLY_SUFFIXES = {".cs", ".fs", ".fsi", ".fsx", ".csproj", ".fsproj", ".props",
                        ".targets", ".sln", ".slnf", ".slnx", ".xaml", ".resx"}
DOTNET_ONLY_NAMES = {"packages.lock.json", "global.json", "NuGet.Config"}


def _load_detector():
    spec = importlib.util.spec_from_file_location("detect_dotnet_changes", DETECTOR_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


DETECTOR = _load_detector()


def _safe_parts(path: str) -> tuple[str, ...] | None:
    parts = PurePosixPath(path).parts
    if not parts or ".." in parts:
        return None
    return parts


def irrelevant_to(language: str, path: str) -> bool:
    """True only when the path is known not to affect the language's analysis."""
    if language not in LANGUAGES:
        raise ValueError(f"Unknown CodeQL language: {language}")
    if DETECTOR.is_documentation(path):
        return True
    parts = _safe_parts(path)
    if parts is None:
        return False
    if language == "csharp":
        return parts[:3] == DASHBOARD_PREFIX and len(parts) > 3
    name = PurePosixPath(path).name
    return PurePosixPath(path).suffix in DOTNET_ONLY_SUFFIXES or name in DOTNET_ONLY_NAMES


def requires_analysis(language: str, paths: list[str]) -> bool:
    return not paths or any(not irrelevant_to(language, path) for path in paths)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--language", required=True, choices=LANGUAGES)
    parser.add_argument("--github-output", type=Path, required=True)
    args = parser.parse_args()
    paths = DETECTOR.changed_paths(args.base, args.head)
    required = requires_analysis(args.language, paths)
    with args.github_output.open("a", encoding="utf-8") as output:
        output.write(f"analyze={'true' if required else 'false'}\n")
    print(f"Inspected {len(paths)} changed paths; CodeQL {args.language} required: {required}")


if __name__ == "__main__":
    main()
