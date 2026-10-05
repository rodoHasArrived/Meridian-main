#!/usr/bin/env python3
"""Check DocFX TOC targets and local archive links against publication mappings.

Source mode needs Python and the existing docs YAML dependency, not DocFX/.NET.
With --site-dir, also require the mapped output files from an already built site.
This is not a rendered-site, external URL, API UID, or Markdown anchor checker.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field
import fnmatch
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

from common import load_data


def glob_matches(path: str, pattern: str) -> bool:
    """Match DocFX-style path globs; * stays within a directory, ** recurses."""
    def match(parts: list[str], patterns: list[str]) -> bool:
        if not patterns:
            return not parts
        if patterns[0] == "**":
            return any(match(parts[index:], patterns[1:]) for index in range(len(parts) + 1))
        return bool(parts) and fnmatch.fnmatchcase(parts[0], patterns[0]) and match(parts[1:], patterns[1:])

    return match(path.split("/"), pattern.removeprefix("./").split("/"))


@dataclass(frozen=True)
class Mapping:
    source: Path
    destination: Path
    patterns: tuple[str, ...]
    excludes: tuple[str, ...]
    kind: str

    def output_for(self, path: Path) -> Path | None:
        if not path.is_relative_to(self.source):
            return None
        relative = path.relative_to(self.source)
        name = relative.as_posix()
        if not any(glob_matches(name, pattern) for pattern in self.patterns):
            return None
        if any(glob_matches(name, pattern) for pattern in self.excludes):
            return None
        output = self.destination / relative
        if self.kind == "content" and path.suffix.lower() in {".md", ".yml", ".yaml"}:
            output = output.with_suffix(".html")
        return output

    def files(self) -> set[Path]:
        return {
            path.resolve()
            for pattern in self.patterns
            for path in self.source.glob(pattern)
            if path.is_file() and self.output_for(path.resolve()) is not None
        }


def read_mappings(config: dict, root: Path) -> list[Mapping]:
    mappings = []
    for kind in ("content", "resource"):
        for entry in config["build"].get(kind, []):
            if not isinstance(entry, dict):
                raise ValueError(f"build.{kind} entries must be file-mapping objects")
            patterns, excludes = entry.get("files", []), entry.get("exclude", [])
            if not isinstance(patterns, list) or not isinstance(excludes, list):
                raise ValueError(f"build.{kind} files/exclude must be lists")
            if any(not isinstance(pattern, str) for pattern in patterns + excludes):
                raise ValueError(f"build.{kind} patterns must be strings")
            destination = Path(entry.get("dest", "."))
            if destination.is_absolute() or ".." in destination.parts:
                raise ValueError(f"build.{kind} dest must remain inside the site")
            mappings.append(Mapping(
                (root / entry.get("src", ".")).resolve(), destination,
                tuple(patterns), tuple(excludes), kind,
            ))
    return mappings


def toc_links(document: object):
    """Yield href, topicHref, tocHref, and homepage from nested TOC entries."""
    if isinstance(document, list):
        for item in document:
            yield from toc_links(item)
    elif isinstance(document, dict):
        for key in ("href", "topicHref", "tocHref", "homepage"):
            if key in document:
                if not isinstance(document[key], str):
                    raise ValueError(f"TOC {key} must be a string")
                yield document[key]
        yield from toc_links(document.get("items", []))
    else:
        raise ValueError("TOC must contain a list or an items object")


def archive_links(markdown: str):
    """Read inline/reference archive links, ignoring fenced examples and inline code."""
    fence = None
    for line in markdown.splitlines():
        marker = re.match(r"^\s*(`{3,}|~{3,})", line)
        if marker:
            if fence is None:
                fence = marker[1]
            elif marker[1][0] == fence[0] and len(marker[1]) >= len(fence):
                fence = None
            continue
        if fence:
            continue
        line = re.sub(r"(`+).*?\1", "", line)
        targets = re.findall(r"\]\(\s*(<[^>]+>|[^\s)]+)", line)
        targets += re.findall(r"^\s*\[[^]]+\]:\s*(<[^>]+>|\S+)", line)
        for target in targets:
            target = target.strip("<>")
            if "archive/docs/" in unquote(urlsplit(target).path):
                yield target


@dataclass
class Result:
    checked: int = 0
    errors: list[str] = field(default_factory=list)
    deferred: set[str] = field(default_factory=set)


def check_navigation(config_path: Path, site_dir: Path | None = None) -> Result:
    config_path = config_path.resolve()
    root = config_path.parent
    config = json.loads(config_path.read_text(encoding="utf-8"))
    mappings = read_mappings(config, root)
    generated_roots = [
        (root / entry["dest"]).resolve()
        for entry in config.get("metadata", []) if "dest" in entry
    ]
    result = Result()

    def display(path: Path) -> str:
        return path.relative_to(root).as_posix() if path.is_relative_to(root) else str(path)

    def check_target(source: Path, href: str) -> None:
        url = urlsplit(href)
        if url.scheme or url.netloc or not url.path:
            return
        target_path = unquote(url.path)
        if target_path.startswith("~/"):
            target = (root / target_path[2:]).resolve()
        elif target_path.startswith("/"):
            result.errors.append(f"{display(source)}: site-root URL is unsupported: {href}")
            return
        else:
            target = (source.parent / target_path).resolve()
        if target.is_dir() or target_path.endswith("/"):
            target /= "toc.yml"
        result.checked += 1
        label = f"{display(source)} -> {href}"
        outputs = [output for mapping in mappings if (output := mapping.output_for(target)) is not None]
        if not outputs:
            result.errors.append(f"{label}: excluded from DocFX content/resources")
        generated = target.suffix in {".yml", ".yaml"} and any(
            target.is_relative_to(directory) for directory in generated_roots
        )
        if not target.is_file():
            if generated and site_dir is None:
                result.deferred.add(display(target))
            elif not generated or site_dir is None:
                result.errors.append(f"{label}: source file is missing")
        if site_dir is not None:
            for output in outputs:
                if not (site_dir / output).is_file():
                    result.errors.append(f"{label}: built output is missing: {output.as_posix()}")

    content = set().union(*(mapping.files() for mapping in mappings if mapping.kind == "content"))
    tocs = sorted(path for path in content if path.name in {"toc.yml", "toc.yaml"})
    if not tocs:
        result.errors.append("No TOC files are included in DocFX content")
    for toc in tocs:
        check_target(toc, toc.name)
        try:
            for href in toc_links(load_data(toc)):
                check_target(toc, href)
        except (ValueError, RuntimeError) as exc:
            result.errors.append(f"{display(toc)}: {exc}")
    for page in sorted(path for path in content if path.suffix == ".md"):
        for href in archive_links(page.read_text(encoding="utf-8")):
            check_target(page, href)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path("docfx.json"))
    parser.add_argument("--site-dir", type=Path, help="Also check files in an already built site")
    parser.add_argument("--summary", action="store_true", help="Print concise check results (default)")
    args = parser.parse_args()
    try:
        result = check_navigation(args.config, args.site_dir)
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as exc:
        print(f"DocFX navigation configuration error: {exc}")
        return 2
    for error in result.errors:
        print(f"ERROR: {error}")
    for target in sorted(result.deferred):
        print(f"DEFERRED: {target} (generated by DocFX metadata; build the site to verify)")
    mode = "source + built files" if args.site_dir else "source/configuration only"
    print(f"DocFX navigation ({mode}): {result.checked} local targets, "
          f"{len(result.errors)} errors, {len(result.deferred)} generated targets deferred")
    return 1 if result.errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
