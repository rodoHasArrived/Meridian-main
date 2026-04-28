#!/usr/bin/env python3
"""Validate runnable command examples in workflow/operator docs against repo reality."""

from __future__ import annotations

import argparse
import difflib
import re
import shlex
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parents[1]
DOC_GLOBS = (
    "docs/HELP.md",
    "docs/development/*.md",
    "README.md",
    "CLAUDE.md",
    "AGENTS.md",
)
COMMAND_PREFIXES = (
    "make ",
    "dotnet ",
    "python ",
    "python3 ",
    "pwsh ",
    "bash ",
    "./scripts/",
    "scripts/",
)
CODE_FENCE_RE = re.compile(r"^```(?P<lang>[A-Za-z0-9_+#-]*)\\s*$")
FLAG_RE = re.compile(r"--[a-z0-9][a-z0-9-]*")
PLACEHOLDER_RE = re.compile(r"^<[^>]+>$")
SPECIAL_CONTEXT_RE = re.compile(r"\\b(TODO|specialized|verify|intended|prerequisite|planned)\\b", re.IGNORECASE)


@dataclass
class CommandExample:
    file_path: Path
    line_number: int
    text: str
    context: str


@dataclass
class Finding:
    severity: str  # fail | warning
    file_path: Path
    line_number: int
    command: str
    reason: str
    suggestion: str | None = None


class ParityChecker:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.known_make_targets = self._collect_make_targets()
        self.known_scripts = self._collect_scripts()
        self.known_projects = self._collect_csproj_paths()
        self.known_cli_flags = self._collect_cli_flags_from_source()

    def _collect_make_targets(self) -> set[str]:
        makefile = self.root / "Makefile"
        targets: set[str] = set()
        for path in [makefile, *(self.root / "make").glob("*.mk")]:
            if not path.exists():
                continue
            for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
                stripped = line.strip()
                if stripped.startswith("#"):
                    continue
                match = re.match(r"^([A-Za-z0-9_.-]+):", stripped)
                if not match:
                    continue
                target = match.group(1)
                if target in {".PHONY", ".DEFAULT_GOAL"}:
                    continue
                targets.add(target)
        return targets

    def _collect_scripts(self) -> set[str]:
        scripts: set[str] = set()
        for path in (self.root / "scripts").rglob("*"):
            if path.is_file():
                scripts.add(str(path.relative_to(self.root)).replace("\\\\", "/"))
        for path in (self.root / "build" / "scripts").rglob("*"):
            if path.is_file():
                scripts.add(str(path.relative_to(self.root)).replace("\\\\", "/"))
        return scripts

    def _collect_csproj_paths(self) -> set[str]:
        return {str(path.relative_to(self.root)).replace("\\\\", "/") for path in self.root.rglob("*.csproj")}

    def _collect_cli_flags_from_source(self) -> set[str]:
        flags: set[str] = set()
        roots = [self.root / "src" / "Meridian", self.root / "src" / "Meridian.Application"]
        for source_root in roots:
            if not source_root.exists():
                continue
            for path in source_root.rglob("*.cs"):
                text = path.read_text(encoding="utf-8", errors="replace")
                for match in FLAG_RE.findall(text):
                    flags.add(match)
        flags.update({"--help"})
        return flags

    def discover_doc_files(self) -> list[Path]:
        files: set[Path] = set()
        for pattern in DOC_GLOBS:
            files.update(self.root.glob(pattern))
        return sorted(path for path in files if path.is_file())

    def extract_examples(self, path: Path) -> list[CommandExample]:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        in_fence = False
        fence_lang = ""
        examples: list[CommandExample] = []

        i = 0
        while i < len(lines):
            line = lines[i]
            match = CODE_FENCE_RE.match(line)
            if match:
                if not in_fence:
                    in_fence = True
                    fence_lang = (match.group("lang") or "").lower()
                else:
                    in_fence = False
                    fence_lang = ""
                i += 1
                continue

            if in_fence and fence_lang in {"", "bash", "sh", "shell", "powershell", "pwsh", "ps1"}:
                cmd_line = self._normalize_command_line(line)
                if cmd_line:
                    context_start = max(0, i - 4)
                    context = "\n".join(lines[context_start:i])
                    examples.append(
                        CommandExample(
                            file_path=path,
                            line_number=i + 1,
                            text=cmd_line,
                            context=context,
                        )
                    )
            i += 1

        return examples

    @staticmethod
    def _normalize_command_line(line: str) -> str | None:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            return None
        for prompt in ("$ ", "PS> ", "> "):
            if stripped.startswith(prompt):
                stripped = stripped[len(prompt):].strip()
        if stripped.endswith("\\"):
            stripped = stripped[:-1].strip()
        if any(stripped.startswith(prefix) for prefix in COMMAND_PREFIXES):
            return stripped
        return None

    def classify(self, example: CommandExample, reason: str, suggestion: str | None) -> Finding:
        severity = "warning" if SPECIAL_CONTEXT_RE.search(example.context) else "fail"
        return Finding(
            severity=severity,
            file_path=example.file_path,
            line_number=example.line_number,
            command=example.text,
            reason=reason,
            suggestion=suggestion,
        )

    def validate_example(self, example: CommandExample) -> list[Finding]:
        findings: list[Finding] = []
        try:
            tokens = shlex.split(example.text)
        except ValueError:
            return [self.classify(example, "Could not parse command line syntax.", None)]
        if not tokens:
            return findings

        head = tokens[0]
        if head == "make":
            findings.extend(self._validate_make(example, tokens))
        elif head == "dotnet":
            findings.extend(self._validate_dotnet(example, tokens))
        elif head in {"python", "python3", "pwsh", "bash"}:
            findings.extend(self._validate_script_invocation(example, tokens[1:]))
        elif head.startswith("./scripts/") or head.startswith("scripts/"):
            findings.extend(self._validate_script_invocation(example, [head]))

        return findings

    def _validate_make(self, example: CommandExample, tokens: list[str]) -> list[Finding]:
        for token in tokens[1:]:
            if token.startswith("-"):
                continue
            if "=" in token:
                continue
            target = token
            if target not in self.known_make_targets:
                suggestion = self._closest(target, self.known_make_targets)
                if suggestion:
                    suggestion = f"make {suggestion}"
                return [self.classify(example, f"Unknown Make target `{target}`.", suggestion)]
            return []
        return [self.classify(example, "No Make target found.", None)]

    def _validate_dotnet(self, example: CommandExample, tokens: list[str]) -> list[Finding]:
        findings: list[Finding] = []

        for i, token in enumerate(tokens):
            if token == "--project" and i + 1 < len(tokens):
                project = tokens[i + 1]
                if not PLACEHOLDER_RE.match(project) and project not in self.known_projects:
                    suggestion = self._closest(project, self.known_projects)
                    findings.append(self.classify(example, f"Project path `{project}` does not exist.", suggestion))

        if "--" in tokens:
            sep = tokens.index("--")
            cli_tokens = tokens[sep + 1 :]
            for token in cli_tokens:
                if not token.startswith("--"):
                    continue
                if PLACEHOLDER_RE.match(token):
                    continue
                flag_name = token.split("=", 1)[0]
                if flag_name not in self.known_cli_flags:
                    suggestion = self._closest(flag_name, self.known_cli_flags)
                    findings.append(self.classify(example, f"CLI flag `{flag_name}` not found in current CLI options.", suggestion))
        return findings

    def _validate_script_invocation(self, example: CommandExample, tail_tokens: list[str]) -> list[Finding]:
        for token in tail_tokens:
            if token.startswith("-") or token.startswith("http://") or token.startswith("https://"):
                continue
            normalized = token.lstrip("./")
            normalized = normalized.replace("\\\\", "/")
            if normalized.startswith("scripts/") or normalized.startswith("build/scripts/"):
                if normalized not in self.known_scripts:
                    suggestion = self._closest(normalized, self.known_scripts)
                    return [self.classify(example, f"Script path `{token}` not found.", suggestion)]
                return []
        return []

    @staticmethod
    def _closest(value: str, candidates: Iterable[str]) -> str | None:
        matches = difflib.get_close_matches(value, list(candidates), n=1, cutoff=0.6)
        return matches[0] if matches else None


def build_report(findings: list[Finding], root: Path) -> str:
    hard_fails = [f for f in findings if f.severity == "fail"]
    warnings = [f for f in findings if f.severity == "warning"]

    lines: list[str] = []
    lines.append("# Workflow Docs Parity Report")
    lines.append("")
    lines.append(f"- Hard fails: {len(hard_fails)}")
    lines.append(f"- Warnings: {len(warnings)}")
    lines.append("")
    if not findings:
        lines.append("All checked documentation commands map to current Make targets, scripts, and CLI flags.")
        return "\n".join(lines) + "\n"

    lines.append("## Findings")
    lines.append("")
    for idx, finding in enumerate(findings, start=1):
        rel = finding.file_path.relative_to(root)
        lines.append(f"{idx}. **{finding.severity.upper()}** `{rel}:{finding.line_number}`")
        lines.append(f"   - Command: `{finding.command}`")
        lines.append(f"   - Issue: {finding.reason}")
        if finding.suggestion:
            lines.append(f"   - Suggested replacement: `{finding.suggestion}`")
        lines.append("")

    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check docs command examples against CLI/help surfaces.")
    parser.add_argument("--report", default="artifacts/docs/workflow-docs-parity-report.md", help="Path for Markdown report output.")
    parser.add_argument("--strict-warnings", action="store_true", help="Treat warnings as hard failures.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    checker = ParityChecker(REPO_ROOT)

    findings: list[Finding] = []
    for doc in checker.discover_doc_files():
        for example in checker.extract_examples(doc):
            findings.extend(checker.validate_example(example))

    findings.sort(key=lambda f: (str(f.file_path), f.line_number, f.command))

    report_path = REPO_ROOT / args.report
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(build_report(findings, REPO_ROOT), encoding="utf-8")

    hard_fails = [f for f in findings if f.severity == "fail"]
    warnings = [f for f in findings if f.severity == "warning"]

    print(f"Report written: {report_path.relative_to(REPO_ROOT)}")
    print(f"Hard fails: {len(hard_fails)}")
    print(f"Warnings: {len(warnings)}")

    if hard_fails:
        return 1
    if args.strict_warnings and warnings:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
