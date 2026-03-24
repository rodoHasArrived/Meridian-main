#!/usr/bin/env python3
"""
TODO Item Creator

Interactive tool to help developers add well-formatted TODO comments to the codebase
with proper metadata, tracking, and consistency.

Features:
- Interactive prompts for TODO details
- Automatic file selection
- GitHub issue integration
- Assignee tagging
- Priority classification
- Template generation
- Validation of TODO format

Usage:
    python3 add-todos.py --file src/MyProject/MyFile.cs
    python3 add-todos.py --interactive
    python3 add-todos.py --template
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

TODO_TYPES = ['TODO', 'FIXME', 'HACK', 'BUG', 'PERF', 'REFACTOR', 'NOTE']

PRIORITY_LEVELS = ['high', 'normal', 'low']

# Comment styles by file extension
COMMENT_STYLES = {
    '.cs': '//',
    '.fs': '//',
    '.fsx': '//',
    '.py': '#',
    '.sh': '#',
    '.bash': '#',
    '.yml': '#',
    '.yaml': '#',
    '.toml': '#',
    '.js': '//',
    '.ts': '//',
    '.tsx': '//',
    '.jsx': '//',
    '.java': '//',
    '.go': '//',
    '.rs': '//',
    '.cpp': '//',
    '.c': '//',
    '.h': '//',
    '.hpp': '//',
    '.sql': '--',
    '.ps1': '#',
    '.psm1': '#',
}


# ---------------------------------------------------------------------------
# Data Models
# ---------------------------------------------------------------------------

@dataclass
class TodoItem:
    """Represents a TODO item to be added."""
    todo_type: str
    description: str
    file_path: Path
    line_number: Optional[int] = None
    issue_number: Optional[int] = None
    assignee: Optional[str] = None
    priority: str = 'normal'

    def format_comment(self, comment_prefix: str) -> str:
        """Format the TODO as a comment string."""
        parts = [f"{comment_prefix} {self.todo_type}:"]

        if self.assignee:
            parts.append(f"@{self.assignee}")

        if self.issue_number:
            parts.append(f"Track with issue #{self.issue_number} -")

        parts.append(self.description)

        return " ".join(parts)


# ---------------------------------------------------------------------------
# Helper Functions
# ---------------------------------------------------------------------------

def get_comment_prefix(file_path: Path) -> str:
    """Get the appropriate comment prefix for a file."""
    ext = file_path.suffix.lower()
    return COMMENT_STYLES.get(ext, '//')


def validate_issue_number(issue_num: str) -> Optional[int]:
    """Validate and parse issue number."""
    if not issue_num:
        return None

    try:
        num = int(issue_num.strip('#'))
        if num > 0:
            return num
    except ValueError:
        pass

    return None


def validate_username(username: str) -> Optional[str]:
    """Validate GitHub username format."""
    if not username:
        return None

    username = username.strip('@')
    if re.match(r'^[a-zA-Z0-9_-]+$', username):
        return username

    return None


def find_files_in_repo(root: Path, pattern: str = '*.cs') -> list[Path]:
    """Find files matching pattern in repository."""
    files = []
    for file in root.rglob(pattern):
        # Skip build artifacts
        if any(part in {'.git', 'bin', 'obj', 'node_modules', '__pycache__'} for part in file.parts):
            continue
        files.append(file)
    return sorted(files)


def insert_todo_at_line(file_path: Path, line_number: int, todo_comment: str) -> bool:
    """Insert TODO comment at specified line in file."""
    try:
        lines = file_path.read_text(encoding='utf-8').splitlines(keepends=True)

        # Adjust line number (1-indexed to 0-indexed)
        insert_pos = max(0, line_number - 1)

        # Add newline if not present
        if not todo_comment.endswith('\n'):
            todo_comment += '\n'

        lines.insert(insert_pos, todo_comment)

        file_path.write_text(''.join(lines), encoding='utf-8')
        return True
    except Exception as e:
        print(f"Error inserting TODO: {e}", file=sys.stderr)
        return False


def append_todo_to_file(file_path: Path, todo_comment: str) -> bool:
    """Append TODO comment to end of file."""
    try:
        with file_path.open('a', encoding='utf-8') as f:
            if not todo_comment.endswith('\n'):
                todo_comment += '\n'
            f.write(todo_comment)
        return True
    except Exception as e:
        print(f"Error appending TODO: {e}", file=sys.stderr)
        return False


# ---------------------------------------------------------------------------
# Interactive Mode
# ---------------------------------------------------------------------------

def prompt_user(message: str, default: str = '') -> str:
    """Prompt user for input with optional default."""
    if default:
        prompt = f"{message} [{default}]: "
    else:
        prompt = f"{message}: "

    response = input(prompt).strip()
    return response if response else default


def prompt_choice(message: str, choices: list[str], default: str = '') -> str:
    """Prompt user to select from a list of choices."""
    print(f"\n{message}")
    for i, choice in enumerate(choices, 1):
        marker = '*' if choice == default else ' '
        print(f"  {marker} {i}. {choice}")

    while True:
        response = input(f"Select [1-{len(choices)}]: ").strip()
        if not response and default:
            return default

        try:
            idx = int(response) - 1
            if 0 <= idx < len(choices):
                return choices[idx]
        except ValueError:
            pass

        print(f"Invalid choice. Please enter 1-{len(choices)}")


def interactive_mode(root: Path) -> Optional[TodoItem]:
    """Run interactive mode to create a TODO item."""
    print("\n=== TODO Item Creator ===\n")

    # Select TODO type
    todo_type = prompt_choice(
        "Select TODO type:",
        TODO_TYPES,
        default='TODO'
    )

    # Get description
    description = prompt_user("Enter description")
    if not description:
        print("Error: Description is required", file=sys.stderr)
        return None

    # Select file
    print("\nEnter file path (relative to repo root):")
    file_str = prompt_user("File path")
    if not file_str:
        print("Error: File path is required", file=sys.stderr)
        return None

    file_path = root / file_str
    if not file_path.exists():
        print(f"Warning: File does not exist: {file_path}", file=sys.stderr)
        create = prompt_user("Create file? (y/n)", default='n')
        if create.lower() != 'y':
            return None

    # Optional: Line number
    line_str = prompt_user("Line number (or press Enter to append to end)", default='')
    line_number = None
    if line_str:
        try:
            line_number = int(line_str)
        except ValueError:
            print("Warning: Invalid line number, will append to end", file=sys.stderr)

    # Optional: Issue number
    issue_str = prompt_user("GitHub issue number (or press Enter to skip)", default='')
    issue_number = validate_issue_number(issue_str)

    # Optional: Assignee
    assignee_str = prompt_user("Assignee username (or press Enter to skip)", default='')
    assignee = validate_username(assignee_str)

    # Priority
    priority = prompt_choice(
        "Select priority:",
        PRIORITY_LEVELS,
        default='normal'
    )

    return TodoItem(
        todo_type=todo_type,
        description=description,
        file_path=file_path,
        line_number=line_number,
        issue_number=issue_number,
        assignee=assignee,
        priority=priority
    )


# ---------------------------------------------------------------------------
# Template Mode
# ---------------------------------------------------------------------------

def print_template() -> None:
    """Print TODO comment templates."""
    print("\n=== TODO Comment Templates ===\n")

    print("Basic TODO:")
    print("  // TODO: Description of what needs to be done\n")

    print("TODO with issue tracking:")
    print("  // TODO: Track with issue #123 - Description\n")

    print("TODO with assignee:")
    print("  // TODO: @username Description\n")

    print("TODO with priority (implicit from type):")
    print("  // FIXME: Critical bug that needs immediate attention")
    print("  // TODO: Nice to have feature")
    print("  // HACK: Temporary workaround\n")

    print("Complete TODO with all metadata:")
    print("  // TODO: @alice Track with issue #456 - Implement retry logic")
    print("  // This is needed because the API occasionally returns 503 errors.\n")

    print("Multi-line TODO:")
    print("  // TODO: Track with issue #789 - Refactor authentication logic")
    print("  // The current implementation has the following issues:")
    print("  // 1. No token refresh mechanism")
    print("  // 2. Credentials stored in plain text")
    print("  // 3. No session timeout handling\n")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main(argv: Optional[list[str]] = None) -> int:  # noqa: C901
    """Entry point."""
    parser = argparse.ArgumentParser(
        description='Interactive tool to add well-formatted TODO comments'
    )
    parser.add_argument(
        '--root', '-r',
        type=Path,
        default=Path('.'),
        help='Repository root directory (default: current directory)'
    )
    parser.add_argument(
        '--file', '-',
        type=Path,
        help='File to add TODO to'
    )
    parser.add_argument(
        '--line', '-l',
        type=int,
        help='Line number to insert TODO at (default: append to end)'
    )
    parser.add_argument(
        '--type', '-t',
        choices=TODO_TYPES,
        default='TODO',
        help='Type of TODO item (default: TODO)'
    )
    parser.add_argument(
        '--description', '-d',
        type=str,
        help='Description of the TODO item'
    )
    parser.add_argument(
        '--issue', '-i',
        type=int,
        help='GitHub issue number to track this TODO'
    )
    parser.add_argument(
        '--assignee', '-a',
        type=str,
        help='GitHub username to assign this TODO to'
    )
    parser.add_argument(
        '--priority', '-p',
        choices=PRIORITY_LEVELS,
        default='normal',
        help='Priority level (default: normal)'
    )
    parser.add_argument(
        '--interactive',
        action='store_true',
        help='Run in interactive mode'
    )
    parser.add_argument(
        '--template',
        action='store_true',
        help='Print TODO comment templates and exit'
    )
    parser.add_argument(
        '--dry-run',
        action='store_true',
        help='Show what would be added without modifying files'
    )

    args = parser.parse_args(argv)

    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    # Template mode
    if args.template:
        print_template()
        return 0

    # Interactive mode
    if args.interactive:
        todo_item = interactive_mode(root)
        if not todo_item:
            return 1
    # Command-line mode
    else:
        if not args.file:
            print("Error: --file is required (or use --interactive)", file=sys.stderr)
            return 1

        if not args.description:
            print("Error: --description is required (or use --interactive)", file=sys.stderr)
            return 1

        file_path = args.file if args.file.is_absolute() else root / args.file

        todo_item = TodoItem(
            todo_type=args.type,
            description=args.description,
            file_path=file_path,
            line_number=args.line,
            issue_number=args.issue,
            assignee=args.assignee,
            priority=args.priority
        )

    # Format the TODO comment
    comment_prefix = get_comment_prefix(todo_item.file_path)
    todo_comment = todo_item.format_comment(comment_prefix)

    # Show what will be added
    print("\nTODO to be added:")
    file_display = (
        todo_item.file_path.relative_to(root)
        if todo_item.file_path.is_relative_to(root)
        else todo_item.file_path
    )
    print(f"  File: {file_display}")
    if todo_item.line_number:
        print(f"  Line: {todo_item.line_number}")
    else:
        print("  Position: End of file")
    print(f"  Comment: {todo_comment}")
    print()

    if args.dry_run:
        print("Dry run - no changes made")
        return 0

    # Add the TODO
    if todo_item.line_number:
        success = insert_todo_at_line(todo_item.file_path, todo_item.line_number, todo_comment)
    else:
        success = append_todo_to_file(todo_item.file_path, todo_comment)

    if success:
        print(f"✓ TODO added successfully to {todo_item.file_path.name}")
        print("\nNext steps:")
        if todo_item.issue_number:
            print(f"  - Review issue #{todo_item.issue_number}")
        else:
            print("  - Consider creating a GitHub issue to track this TODO")
        print("  - Run: python3 build/scripts/docs/scan-todos.py to update TODO tracking")
        return 0
    else:
        print("✗ Failed to add TODO", file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
