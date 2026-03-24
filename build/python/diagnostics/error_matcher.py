import json
import re
from pathlib import Path

from core.utils import colorize


class ErrorMatcher:
    def __init__(self, knowledge_dir: Path) -> None:
        self.knowledge_dir = knowledge_dir
        self.patterns = self._load_patterns()

    def _load_patterns(self) -> list[dict]:
        patterns = []
        if not self.knowledge_dir.exists():
            return patterns
        for path in sorted(self.knowledge_dir.glob("*.json")):
            payload = json.loads(path.read_text(encoding="utf-8"))
            patterns.append(payload)
        return patterns

    def match(self, output: str) -> list[dict]:
        matches = []
        for pattern in self.patterns:
            for entry in pattern.get("patterns", []):
                regex = entry.get("pattern")
                if regex and re.search(regex, output, re.MULTILINE):
                    matches.append(pattern)
                    break
        return matches

    def print_matches(self, matches: list[dict]) -> None:  # noqa: C901
        if not matches:
            print(colorize("No known error patterns detected.", "0;32"))
            return
        for match in matches:
            severity = match.get("severity", "info")
            color = "0;34"
            if severity == "error":
                color = "0;31"
            elif severity == "warning":
                color = "1;33"
            print(colorize(f"[{severity.upper()}] {match.get('error_code')}", color))
            diagnosis = match.get("diagnosis", {})
            remediation = match.get("remediation", {})
            print(f"  Summary: {diagnosis.get('summary', '')}")
            print(f"  Explanation: {diagnosis.get('explanation', '')}")
            if diagnosis.get("investigation_steps"):
                print("  Investigation:")
                for step in diagnosis["investigation_steps"]:
                    print(f"    - {step.get('description')}: {step.get('command')}")
            if remediation:
                print("  Remediation:")
                if remediation.get("auto_fixable"):
                    print(f"    Auto-fix: {remediation.get('auto_fix_command')}")
                for step in remediation.get("manual_steps", []):
                    print(f"    - {step}")
                if remediation.get("prevention"):
                    print("    Prevention:")
                    for item in remediation["prevention"]:
                        print(f"      - {item}")
            if match.get("related_docs"):
                print("  Docs:")
                for doc in match["related_docs"]:
                    print(f"    - {doc}")
            if match.get("examples"):
                print("  Examples:")
                for example in match["examples"]:
                    print(f"    - {example}")
            print("")
