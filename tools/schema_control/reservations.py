"""Pending migration ordinals, safe scaffolding, and generated planning docs.

Reservation ordinals are an authoring policy, independent of database ledgers.
Applied migration filenames are never rewritten by this module.
"""

from __future__ import annotations

import json
import os
import re
import string
import tempfile
from collections import defaultdict
from collections.abc import Mapping
from contextlib import contextmanager
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any

from .common import Finding, sha256_text
from .migrations import (
    BaseFileReader,
    MigrationFile,
    MigrationInventory,
    build_migration_inventory,
)


DOC_PATH = "docs/engineering/blueprints/README.md"
START_MARKER = "<!-- migration-reservations:start -->"
END_MARKER = "<!-- migration-reservations:end -->"
_SLUG = re.compile(r"[a-z][a-z0-9]*(?:_[a-z0-9]+)*\Z")


class ReservationError(ValueError):
    """A reservation register or scaffold request cannot be used safely."""


@dataclass(frozen=True, slots=True)
class MigrationReservation:
    ordinal: int
    slug: str
    description: str
    blueprint: str


@dataclass(frozen=True, slots=True)
class MigrationReservationSet:
    id: str
    filename_template: str
    ordinal_pattern: str
    reservations: tuple[MigrationReservation, ...]

    def filename(self, ordinal: int, slug: str) -> str:
        return self.filename_template.format(ordinal=ordinal, slug=slug)

    def parse_ordinal(self, filename: str) -> int | None:
        match = re.match(self.ordinal_pattern, filename)
        if match is None:
            return None
        raw = match.group("ordinal")
        if raw is None or re.fullmatch(r"[0-9]+", raw) is None:
            return None
        ordinal = int(raw)
        return ordinal if ordinal > 0 else None


@dataclass(frozen=True, slots=True)
class MigrationReservationRegister:
    path: str
    migration_sets: tuple[MigrationReservationSet, ...]

    def set_for(self, migration_set_id: str) -> MigrationReservationSet:
        for item in self.migration_sets:
            if item.id == migration_set_id:
                return item
        raise ReservationError(f"Unknown migration set '{migration_set_id}'.")


def _repository_path(root: Path, value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ReservationError(f"{label} must be a repository-relative path.")
    path = Path(value)
    if path.is_absolute() or ".." in path.parts or "\\" in value:
        raise ReservationError(f"{label} must stay within the repository: {value}")
    try:
        return (root / path).resolve().relative_to(root.resolve()).as_posix()
    except ValueError as exc:
        raise ReservationError(f"{label} escapes the repository: {value}") from exc


def _mapping(value: Any, fields: set[str], label: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping) or set(value) != fields:
        raise ReservationError(f"{label} must contain exactly {', '.join(sorted(fields))}.")
    return value


def _nonempty(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ReservationError(f"{label} must be a non-empty string.")
    return value


def _slug(value: Any) -> str:
    if not isinstance(value, str) or _SLUG.fullmatch(value) is None:
        raise ReservationError(
            "Migration name must be a lowercase snake_case slug starting with a letter."
        )
    return value


def _ordinal(value: Any) -> int:
    if type(value) is not int or value <= 0:
        raise ReservationError("Migration ordinal must be a positive integer.")
    return value


def _unique_json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ReservationError(f"Reservation register repeats JSON key '{key}'.")
        result[key] = value
    return result


def _validate_naming(template: str, pattern: str, set_id: str) -> None:
    try:
        fields = list(string.Formatter().parse(template))
        names = [name for _, name, _, _ in fields if name is not None]
        if sorted(names) != ["ordinal", "slug"]:
            raise ValueError("template must contain ordinal and slug once each")
        if any(
            conversion or (name == "slug" and spec)
            for _, name, spec, conversion in fields
        ):
            raise ValueError("template has unsupported conversions or slug formatting")
        rendered = template.format(ordinal=1, slug="example")
        if (
            Path(rendered).name != rendered
            or "/" in rendered
            or "\\" in rendered
            or not rendered.endswith(".sql")
        ):
            raise ValueError("template must render one SQL filename")
        compiled = re.compile(pattern)
        if "ordinal" not in compiled.groupindex or not pattern.startswith("^"):
            raise ValueError("pattern must be anchored and contain named ordinal group")
        for number in (1, 1001):
            match = compiled.match(template.format(ordinal=number, slug="example"))
            if match is None or match.group("ordinal") != str(number).zfill(
                len(match.group("ordinal") or "")
            ):
                raise ValueError("template and ordinal pattern disagree")
    except (ValueError, KeyError, IndexError, re.error) as exc:
        raise ReservationError(
            f"Migration set '{set_id}' has invalid naming rules: {exc}"
        ) from exc


def load_reservation_register(
    root: Path, config: Mapping[str, Any]
) -> MigrationReservationRegister | None:
    """Load and strictly validate the configured register; absent config opts out."""
    if "migration_reservations" not in config:
        return None
    root = root.resolve()
    relative = _repository_path(
        root, config["migration_reservations"], "Reservation register"
    )
    try:
        raw = json.loads(
            (root / relative).read_text(encoding="utf-8"),
            object_pairs_hook=_unique_json_pairs,
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ReservationError(
            f"Cannot read reservation register '{relative}': {exc}"
        ) from exc
    raw = _mapping(raw, {"version", "migration_sets"}, "Reservation register")
    if type(raw["version"]) is not int or raw["version"] != 1:
        raise ReservationError("Reservation register version must be 1.")
    if not isinstance(raw["migration_sets"], list):
        raise ReservationError("Reservation migration_sets must be a list.")
    inventory = build_migration_inventory(root, config)
    known = {item.id for item in inventory.migration_sets}
    sets: list[MigrationReservationSet] = []
    seen: set[str] = set()
    for entry in raw["migration_sets"]:
        entry = _mapping(
            entry,
            {"id", "filename_template", "ordinal_pattern", "reservations"},
            "Reservation migration set",
        )
        set_id = _nonempty(entry["id"], "Migration set id")
        if set_id not in known or set_id in seen:
            raise ReservationError(
                f"Unknown or duplicate reservation migration set '{set_id}'."
            )
        seen.add(set_id)
        template = _nonempty(entry["filename_template"], "Filename template")
        pattern = _nonempty(entry["ordinal_pattern"], "Ordinal pattern")
        _validate_naming(template, pattern, set_id)
        if not isinstance(entry["reservations"], list):
            raise ReservationError(f"Migration set '{set_id}' reservations must be a list.")
        reservations: list[MigrationReservation] = []
        occupied: set[int] = set()
        for reservation in entry["reservations"]:
            reservation = _mapping(
                reservation,
                {"ordinal", "slug", "description", "blueprint"},
                "Migration reservation",
            )
            number = _ordinal(reservation["ordinal"])
            if number in occupied:
                raise ReservationError(
                    f"Migration set '{set_id}' repeats reserved ordinal {number}."
                )
            occupied.add(number)
            blueprint = _repository_path(
                root, reservation["blueprint"], "Reservation blueprint"
            )
            if not (root / blueprint).is_file():
                raise ReservationError(f"Reservation blueprint does not exist: {blueprint}")
            reservations.append(
                MigrationReservation(
                    number,
                    _slug(reservation["slug"]),
                    _nonempty(reservation["description"], "Reservation description"),
                    blueprint,
                )
            )
        sets.append(
            MigrationReservationSet(
                set_id, template, pattern,
                tuple(sorted(reservations, key=lambda item: item.ordinal)),
            )
        )
    if seen != known:
        raise ReservationError(
            "Reservation register omits migration sets: " + ", ".join(sorted(known - seen))
        )
    return MigrationReservationRegister(
        relative, tuple(sorted(sets, key=lambda item: item.id))
    )


def reservation_findings(
    inventory: MigrationInventory,
    register: MigrationReservationRegister | None,
    base_reader: BaseFileReader | None = None,
) -> list[Finding]:
    """Check naming, occupied claims, and collisions introduced since the base.

    Baseline-free inventory preserves historical duplicates. With a baseline,
    duplicate groups pass only when every exact path already exists in the base.
    """
    if register is None:
        return []
    findings: list[Finding] = []
    for migration_set in register.migration_sets:
        groups: dict[int, list[MigrationFile]] = defaultdict(list)
        reserved = {item.ordinal for item in migration_set.reservations}
        for item in inventory.files_for(migration_set.id):
            number = migration_set.parse_ordinal(item.filename)
            if number is None:
                findings.append(
                    Finding(
                        "migration-reservation-ordinal-invalid", "error",
                        "Filename does not match positive ordinal pattern "
                        f"'{migration_set.ordinal_pattern}'.",
                        item.path, migration_set.id,
                    )
                )
                continue
            groups[number].append(item)
            if number in reserved:
                findings.append(
                    Finding(
                        "migration-reservation-occupied", "error",
                        f"Migration ordinal {number} is both occupied and reserved; "
                        "retire the pending reservation before implementing it.",
                        item.path, migration_set.id,
                    )
                )
        if base_reader is not None:
            for number, items in sorted(groups.items()):
                if len(items) < 2:
                    continue
                introduced = [item.path for item in items if base_reader(item.path) is None]
                if introduced:
                    findings.append(
                        Finding(
                            "migration-ordinal-duplicate", "error",
                            f"New migration ordinal {number} collision: "
                            + ", ".join(sorted(item.filename for item in items)),
                            sorted(introduced)[0], migration_set.id,
                        )
                    )
    return sorted(findings, key=lambda item: (item.rule_id, item.path or "", item.message))


def _validated_inventory(
    root: Path, config: Mapping[str, Any], register: MigrationReservationRegister
) -> MigrationInventory:
    inventory = build_migration_inventory(root, config)
    errors = [
        item
        for item in (*inventory.findings, *reservation_findings(inventory, register))
        if item.severity == "error"
    ]
    if errors:
        raise ReservationError(
            "Invalid migration inventory: " + "; ".join(item.message for item in errors)
        )
    return inventory


def _cell(value: str) -> str:
    return value.replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def _prepared_docs(
    root: Path, inventory: MigrationInventory, register: MigrationReservationRegister
) -> tuple[Path, str]:
    relative = _repository_path(root, DOC_PATH, "Migration documentation")
    path = root / relative
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise ReservationError(
            f"Cannot read migration documentation '{relative}': {exc}"
        ) from exc
    if (
        text.count(START_MARKER) != 1
        or text.count(END_MARKER) != 1
        or text.index(START_MARKER) >= text.index(END_MARKER)
    ):
        raise ReservationError(
            f"'{relative}' must contain exactly one ordered "
            "migration-reservations marker pair."
        )
    lines = [
        "",
        "<!-- Generated by schema-control generate-migration-docs; "
        "do not edit this block. -->",
    ]
    ledger = next((item for item in register.migration_sets if item.id == "ledger"), None)
    if ledger is not None:
        files = inventory.files_for("ledger")
        if files:
            highest = max(
                files,
                key=lambda item: (ledger.parse_ordinal(item.filename) or 0, item.filename),
            )
            lines.extend(["", f"**Highest Ledger ordinal on disk: `{highest.filename}`.**"])
        historical = sorted(
            item.filename for item in files if ledger.parse_ordinal(item.filename) == 8
        )
        if len(historical) > 1:
            lines.extend([
                "",
                "Historical Ledger ordinal `008` is shared by "
                + " and ".join(f"`{name}`" for name in historical)
                + ". Preserve these applied filenames and ordinals.",
            ])
    lines.extend([
        "",
        "| Migration set | Reserved ordinal | Proposed filename | Blueprint / purpose |",
        "|---|---:|---|---|",
    ])
    for migration_set in register.migration_sets:
        for item in migration_set.reservations:
            target = Path(os.path.relpath(root / item.blueprint, path.parent)).as_posix()
            lines.append(
                f"| `{_cell(migration_set.id)}` | `{item.ordinal:03d}` | "
                f"`{_cell(migration_set.filename(item.ordinal, item.slug))}` | "
                f"[{_cell(item.description)}]({target}) |"
            )
    if not any(item.reservations for item in register.migration_sets):
        lines.append("| — | — | — | No pending reservations. |")
    block = "\n".join(lines) + "\n\n"
    start = text.index(START_MARKER) + len(START_MARKER)
    end = text.index(END_MARKER)
    return path, text[:start] + block + text[end:]


def _atomic_write(path: Path, text: str) -> None:
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n",
            dir=path.parent, prefix=".migration-docs-", delete=False,
        ) as handle:
            temporary = handle.name
            handle.write(text)
        os.replace(temporary, path)
    finally:
        if temporary is not None and Path(temporary).exists():
            Path(temporary).unlink()


@contextmanager
def _repository_lock(root: Path):
    # Keep the lock inode stable so queued invocations cannot acquire different
    # locks after unlinking. The lock never adds an untracked repository file.
    path = Path(tempfile.gettempdir()) / (
        f"meridian-migration-{sha256_text(str(root.resolve()))}.lock"
    )
    descriptor = os.open(path, os.O_CREAT | os.O_RDWR, 0o600)
    locked = False
    try:
        if os.name == "nt":
            import msvcrt

            if os.fstat(descriptor).st_size == 0:
                os.write(descriptor, b"\0")
            os.lseek(descriptor, 0, os.SEEK_SET)
            msvcrt.locking(descriptor, msvcrt.LK_LOCK, 1)
        else:
            import fcntl

            fcntl.flock(descriptor, fcntl.LOCK_EX)
        locked = True
        yield
    finally:
        if locked:
            if os.name == "nt":
                os.lseek(descriptor, 0, os.SEEK_SET)
                msvcrt.locking(descriptor, msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def generate_migration_docs(
    root: Path, config: Mapping[str, Any], check: bool = False
) -> bool:
    """Generate the reservation block. Check returns clean; write returns changed."""
    root = root.resolve()
    with _repository_lock(root):
        register = load_reservation_register(root, config)
        if register is None:
            raise ReservationError(
                "Configure migration_reservations before generating migration documentation."
            )
        inventory = _validated_inventory(root, config, register)
        path, rendered = _prepared_docs(root, inventory, register)
        changed = path.read_text(encoding="utf-8") != rendered
        if check:
            return not changed
        if changed:
            _atomic_write(path, rendered)
        return changed


def scaffold_migration(
    root: Path,
    config: Mapping[str, Any],
    migration_set_id: str,
    name: str,
    ordinal: int | None = None,
) -> Path:
    """Create one free, append-only SQL migration and refresh reservation docs."""
    root = root.resolve()
    name = _slug(name)
    if ordinal is not None:
        _ordinal(ordinal)
    with _repository_lock(root):
        register = load_reservation_register(root, config)
        if register is None:
            raise ReservationError(
                "Configure migration_reservations before scaffolding migrations."
            )
        rules = register.set_for(migration_set_id)
        inventory = _validated_inventory(root, config, register)
        migration_set = next(
            item for item in inventory.migration_sets if item.id == migration_set_id
        )
        occupied = {
            rules.parse_ordinal(item.filename)
            for item in inventory.files_for(migration_set_id)
        }
        reserved = {item.ordinal for item in rules.reservations}
        if ordinal is None:
            ordinal = max(occupied, default=0) + 1
            while ordinal in reserved:
                ordinal += 1
        if ordinal in occupied or ordinal in reserved:
            state = "occupied" if ordinal in occupied else "reserved"
            raise ReservationError(
                f"Migration ordinal {ordinal} is {state} in '{migration_set_id}'."
            )
        filename = rules.filename(ordinal, name)
        relative = _repository_path(
            root, str(Path(migration_set.directory) / filename), "Scaffold migration"
        )
        path = root / relative
        planned = MigrationFile(
            migration_set_id, migration_set.schema, relative, filename,
            ordinal, "", migration_set.immutable,
        )
        docs_path, docs_text = _prepared_docs(
            root, replace(inventory, files=(*inventory.files, planned)), register
        )
        sql = (
            f"-- Migration: {migration_set_id} / {name}\n"
            "-- Append-only: never rename or renumber applied migrations.\n"
            "-- TODO: replace this no-op with DDL using the __SCHEMA__ placeholder.\n"
            "-- The migration runner owns the transaction.\n\nSELECT 1;\n"
        )
        created = False
        try:
            with path.open("x", encoding="utf-8", newline="\n") as handle:
                created = True
                handle.write(sql)
        except OSError as exc:
            if created:
                path.unlink()
            raise ReservationError(f"Cannot create migration '{relative}': {exc}") from exc
        try:
            if docs_path.read_text(encoding="utf-8") != docs_text:
                _atomic_write(docs_path, docs_text)
        except Exception:
            path.unlink()
            raise
        return path
