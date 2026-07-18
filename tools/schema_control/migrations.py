"""Migration inventory, safety checks, and fresh-database application.

Configuration is supplied as an already parsed dictionary. The canonical shape is::

    {
        "migration_sets": [
            {
                "id": "security-master",
                "directory": "src/Meridian.Storage/SecurityMaster/Migrations",
                "schema": "security_master",
                "glob": "*.sql",
                "track_ordinals": True,
                "ordinal_pattern": r"^(?P<ordinal>\d+)_",
                "immutable": True,
                "quote_schema": False,
                "ledger": {
                    "table": "schema_migrations",
                    "key_column": "filename",
                    "checksum_column": "checksum",
                },
            }
        ],
        "migration_search_roots": ["src"],
    }

``migrations`` is accepted as an alias for ``migration_sets``. Migration-set
entries may also be a mapping keyed by set id. All paths in returned inventory
objects are repository-relative POSIX paths.
"""

from __future__ import annotations

import re
import subprocess
from collections.abc import Callable, Iterable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol

from .common import Finding, normalize_text, sha256_bytes


_IDENTIFIER_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_DEFAULT_ORDINAL_PATTERN = r"^(?P<ordinal>\d+)(?:_|__)"
_IGNORED_DIRECTORY_PARTS = frozenset(
    {
        ".git",
        ".vs",
        "__pycache__",
        "artifacts",
        "bin",
        "node_modules",
        "obj",
        "testresults",
    }
)


class BaseFileReader(Protocol):
    """Read a repository-relative file from a comparison baseline.

    Return ``None`` when the path did not exist in the baseline.
    """

    def __call__(self, relative_path: str) -> bytes | str | None: ...


class DatabaseConnector(Protocol):
    """Create a DB-API-like connection for a PostgreSQL URL."""

    def __call__(self, database_url: str) -> Any: ...


@dataclass(frozen=True, slots=True)
class MigrationLedger:
    """Migration ledger shape for one migration set."""

    table: str = "schema_migrations"
    key_column: str = "filename"
    checksum_column: str = "checksum"
    ordinal_column: str = "ordinal"
    checksum_required: bool = False


@dataclass(frozen=True, slots=True)
class MigrationSet:
    """Configuration for an ordered collection of SQL migration files."""

    id: str
    directory: str
    schema: str
    file_glob: str = "*.sql"
    track_ordinals: bool = False
    ordinal_pattern: str = _DEFAULT_ORDINAL_PATTERN
    immutable: bool = True
    quote_schema: bool = False
    ledger: MigrationLedger = MigrationLedger()

    @property
    def rendered_schema(self) -> str:
        """Return the schema token used to replace ``__SCHEMA__``."""

        validate_identifier(self.schema, "schema")
        return quote_identifier(self.schema) if self.quote_schema else self.schema


@dataclass(frozen=True, slots=True)
class MigrationFile:
    """One deterministic migration-inventory entry."""

    migration_set_id: str
    schema: str
    path: str
    filename: str
    ordinal: int | None
    sha256: str
    immutable: bool

    def to_dict(self) -> dict[str, Any]:
        return {
            "migration_set_id": self.migration_set_id,
            "schema": self.schema,
            "path": self.path,
            "filename": self.filename,
            "ordinal": self.ordinal,
            "sha256": self.sha256,
            "immutable": self.immutable,
        }


@dataclass(frozen=True, slots=True)
class MigrationInventory:
    """Resolved migration sets, files, and inventory findings."""

    root: Path
    migration_sets: tuple[MigrationSet, ...]
    files: tuple[MigrationFile, ...]
    findings: tuple[Finding, ...]

    @property
    def has_errors(self) -> bool:
        return any(finding.severity == "error" for finding in self.findings)

    def files_for(self, migration_set_id: str) -> tuple[MigrationFile, ...]:
        return tuple(
            item for item in self.files if item.migration_set_id == migration_set_id
        )

    def to_dict(self) -> dict[str, Any]:
        return {
            "migration_sets": [
                {
                    "id": item.id,
                    "directory": item.directory,
                    "schema": item.schema,
                    "glob": item.file_glob,
                    "track_ordinals": item.track_ordinals,
                    "immutable": item.immutable,
                    "quote_schema": item.quote_schema,
                    "ledger": {
                        "table": item.ledger.table,
                        "key_column": item.ledger.key_column,
                        "checksum_column": item.ledger.checksum_column,
                        "ordinal_column": item.ledger.ordinal_column,
                        "checksum_required": item.ledger.checksum_required,
                    },
                }
                for item in self.migration_sets
            ],
            "files": [item.to_dict() for item in self.files],
            "findings": [item.to_dict() for item in self.findings],
        }


@dataclass(frozen=True, slots=True)
class DestructivePattern:
    """A stable destructive-migration rule and its SQL regex."""

    rule_id: str
    expression: str
    message: str


DEFAULT_DESTRUCTIVE_PATTERNS: tuple[DestructivePattern, ...] = (
    DestructivePattern(
        "migration-drop-object",
        r"\bdrop\s+(?:database|schema|foreign\s+table|table|domain|type|materialized\s+view|view|sequence|function|procedure|trigger|policy|index|extension|collation)\b",
        "New migration drops a PostgreSQL object.",
    ),
    DestructivePattern(
        "migration-drop-table-member",
        r"\balter\s+table\b[^;]{0,1000}?\bdrop\s+(?:column|constraint)\b",
        "New migration drops a table column or constraint.",
    ),
    DestructivePattern(
        "migration-truncate",
        r"\btruncate(?:\s+table)?\b",
        "New migration truncates table data.",
    ),
    DestructivePattern(
        "migration-rename",
        r"\balter\s+(?:table|view|materialized\s+view|sequence|type|domain|index|schema)\b[^;]{0,1000}?\brename\s+(?:column\b|value\b|attribute\b|constraint\b|to\b)",
        "New migration renames a PostgreSQL object or member and may require expand-and-contract rollout.",
    ),
    DestructivePattern(
        "migration-alter-column-type",
        r"\balter\s+table\b[^;]{0,1000}?\balter\s+column\b[^;]{0,500}?\btype\b",
        "New migration changes a column type and may rewrite or reject existing data.",
    ),
    DestructivePattern(
        "migration-detach-partition",
        r"\balter\s+table\b[^;]{0,1000}?\bdetach\s+partition\b",
        "New migration detaches a table partition.",
    ),
)


@dataclass(frozen=True, slots=True)
class MigrationApplyResult:
    """Files applied or skipped by :func:`apply_migrations`."""

    applied: tuple[str, ...]
    skipped: tuple[str, ...]


class MigrationConfigurationError(ValueError):
    """Raised when migration configuration cannot be used safely."""


class MigrationApplyError(RuntimeError):
    """Raised when fresh-database migration application fails validation."""


def validate_identifier(value: str, label: str) -> str:
    """Validate and return a PostgreSQL identifier used in rendered SQL."""

    if not _IDENTIFIER_RE.fullmatch(value):
        raise MigrationConfigurationError(
            f"Unsupported PostgreSQL {label} identifier '{value}'. "
            "Use letters, digits, and underscores, and start with a letter or underscore."
        )
    return value


def quote_identifier(value: str) -> str:
    """Quote an already validated PostgreSQL identifier."""

    return f'"{validate_identifier(value, "quoted")}"'


def _relative_directory(root: Path, value: Any) -> str:
    if not isinstance(value, str) or not value.strip():
        raise MigrationConfigurationError(
            "Migration directory must be a non-empty repository-relative path."
        )
    requested = Path(value)
    if requested.is_absolute():
        raise MigrationConfigurationError(
            f"Migration directory must be repository-relative: {value}"
        )
    resolved_root = root.resolve()
    resolved = (resolved_root / requested).resolve()
    try:
        return resolved.relative_to(resolved_root).as_posix()
    except ValueError as exc:
        raise MigrationConfigurationError(
            f"Migration directory escapes repository root: {value}"
        ) from exc


def _migration_set_entries(config: Mapping[str, Any]) -> list[Mapping[str, Any]]:
    raw = config.get("migration_sets", config.get("migrations", []))
    if isinstance(raw, Mapping):
        entries: list[Mapping[str, Any]] = []
        for item_id, value in raw.items():
            if not isinstance(value, Mapping):
                raise MigrationConfigurationError(
                    f"Migration set '{item_id}' must be a mapping."
                )
            merged = dict(value)
            merged.setdefault("id", str(item_id))
            entries.append(merged)
        return entries
    if not isinstance(raw, Sequence) or isinstance(raw, (str, bytes, bytearray)):
        raise MigrationConfigurationError("'migration_sets' must be a list or mapping.")
    if not all(isinstance(item, Mapping) for item in raw):
        raise MigrationConfigurationError(
            "Every migration-set entry must be a mapping."
        )
    return list(raw)


def _parse_migration_set(root: Path, raw: Mapping[str, Any]) -> MigrationSet:
    migration_set_id = str(raw.get("id", "")).strip()
    if not migration_set_id:
        raise MigrationConfigurationError("Migration set id cannot be empty.")
    directory = _relative_directory(root, raw.get("directory"))
    schema = validate_identifier(str(raw.get("schema", "")).strip(), "schema")

    ledger_raw = raw.get("ledger", {})
    if not isinstance(ledger_raw, Mapping):
        raise MigrationConfigurationError(
            f"Migration set '{migration_set_id}' ledger must be a mapping."
        )
    ledger = MigrationLedger(
        table=validate_identifier(
            str(ledger_raw.get("table", "schema_migrations")), "ledger table"
        ),
        key_column=validate_identifier(
            str(ledger_raw.get("key_column", "filename")), "ledger key column"
        ),
        checksum_column=validate_identifier(
            str(ledger_raw.get("checksum_column", "checksum")),
            "ledger checksum column",
        ),
        ordinal_column=validate_identifier(
            str(ledger_raw.get("ordinal_column", "ordinal")),
            "ledger ordinal column",
        ),
        checksum_required=bool(ledger_raw.get("checksum_required", False)),
    )

    file_glob = str(raw.get("glob", "*.sql")).strip()
    if not file_glob or Path(file_glob).is_absolute() or ".." in Path(file_glob).parts:
        raise MigrationConfigurationError(
            f"Migration set '{migration_set_id}' has unsafe glob '{file_glob}'."
        )

    ordinal_pattern = str(raw.get("ordinal_pattern", _DEFAULT_ORDINAL_PATTERN))
    try:
        re.compile(ordinal_pattern)
    except re.error as exc:
        raise MigrationConfigurationError(
            f"Migration set '{migration_set_id}' has invalid ordinal_pattern: {exc}"
        ) from exc

    return MigrationSet(
        id=migration_set_id,
        directory=directory,
        schema=schema,
        file_glob=file_glob,
        track_ordinals=bool(raw.get("track_ordinals", False)),
        ordinal_pattern=ordinal_pattern,
        immutable=bool(raw.get("immutable", True)),
        quote_schema=bool(raw.get("quote_schema", False)),
        ledger=ledger,
    )


def _parse_ordinal(migration_set: MigrationSet, filename: str) -> int | None:
    if not migration_set.track_ordinals:
        return None
    match = re.search(migration_set.ordinal_pattern, filename)
    if match is None:
        return None
    raw = match.groupdict().get("ordinal")
    if raw is None and match.groups():
        raw = match.group(1)
    if raw is None:
        return None
    try:
        return int(raw)
    except ValueError:
        return None


def _finding_sort_key(finding: Finding) -> tuple[str, str, str, str]:
    return (finding.severity, finding.rule_id, finding.path or "", finding.message)


def build_migration_inventory(
    root: Path, config: Mapping[str, Any]
) -> MigrationInventory:
    """Resolve configured migration sets and report inventory-policy findings.

    Unique ordinals are enforced only for sets with ``track_ordinals: true``.
    Directories named ``Migrations`` containing SQL files under configured search
    roots are reported when no migration set registers them.
    """

    root = root.resolve()
    findings: list[Finding] = []
    migration_sets: list[MigrationSet] = []
    seen_ids: set[str] = set()

    for index, raw in enumerate(_migration_set_entries(config)):
        try:
            migration_set = _parse_migration_set(root, raw)
        except MigrationConfigurationError as exc:
            findings.append(
                Finding(
                    "migration-config-invalid",
                    "error",
                    str(exc),
                    subject=f"migration_sets[{index}]",
                )
            )
            continue
        if migration_set.id in seen_ids:
            findings.append(
                Finding(
                    "migration-set-id-duplicate",
                    "error",
                    f"Migration set id '{migration_set.id}' is registered more than once.",
                    path=migration_set.directory,
                    subject=migration_set.id,
                )
            )
            continue
        seen_ids.add(migration_set.id)
        migration_sets.append(migration_set)

    ledger_owners: dict[tuple[str, str], str] = {}
    for migration_set in migration_sets:
        ledger_identity = (migration_set.schema, migration_set.ledger.table)
        previous_owner = ledger_owners.get(ledger_identity)
        if previous_owner is not None:
            findings.append(
                Finding(
                    "migration-ledger-collision",
                    "error",
                    f"Migration sets '{previous_owner}' and '{migration_set.id}' share ledger "
                    f"{migration_set.schema}.{migration_set.ledger.table}.",
                    path=migration_set.directory,
                    subject=migration_set.id,
                )
            )
        else:
            ledger_owners[ledger_identity] = migration_set.id

    files: list[MigrationFile] = []
    for migration_set in migration_sets:
        directory = root / migration_set.directory
        if not directory.is_dir():
            findings.append(
                Finding(
                    "migration-directory-missing",
                    "error",
                    f"Registered migration directory does not exist: {migration_set.directory}",
                    path=migration_set.directory,
                    subject=migration_set.id,
                )
            )
            continue

        paths = sorted(
            (
                item
                for item in directory.glob(migration_set.file_glob)
                if item.is_file()
            ),
            key=lambda item: (item.name.casefold(), item.name),
        )
        if not paths:
            findings.append(
                Finding(
                    "migration-directory-empty",
                    "warning",
                    f"Migration directory contains no files matching '{migration_set.file_glob}'.",
                    path=migration_set.directory,
                    subject=migration_set.id,
                )
            )

        ordinal_owners: dict[int, str] = {}
        for path in paths:
            relative_path = path.resolve().relative_to(root).as_posix()
            ordinal = _parse_ordinal(migration_set, path.name)
            if migration_set.track_ordinals and ordinal is None:
                findings.append(
                    Finding(
                        "migration-ordinal-missing",
                        "error",
                        f"Migration filename does not match ordinal pattern '{migration_set.ordinal_pattern}'.",
                        path=relative_path,
                        subject=migration_set.id,
                    )
                )
            elif ordinal is not None:
                previous = ordinal_owners.get(ordinal)
                if previous is not None:
                    findings.append(
                        Finding(
                            "migration-ordinal-duplicate",
                            "error",
                            f"Migration ordinal {ordinal} is shared by '{previous}' and '{path.name}'.",
                            path=relative_path,
                            subject=migration_set.id,
                        )
                    )
                else:
                    ordinal_owners[ordinal] = path.name

            files.append(
                MigrationFile(
                    migration_set_id=migration_set.id,
                    schema=migration_set.schema,
                    path=relative_path,
                    filename=path.name,
                    ordinal=ordinal,
                    sha256=sha256_bytes(path.read_bytes()),
                    immutable=migration_set.immutable,
                )
            )

    if bool(config.get("detect_unregistered_migration_directories", True)):
        search_roots_raw = config.get("migration_search_roots", ["src"])
        if not isinstance(search_roots_raw, Sequence) or isinstance(
            search_roots_raw, (str, bytes, bytearray)
        ):
            raise MigrationConfigurationError(
                "'migration_search_roots' must be a list of paths."
            )
        findings.extend(
            detect_unregistered_migration_directories(
                root,
                registered_directories=[item.directory for item in migration_sets],
                search_roots=[str(item) for item in search_roots_raw],
            )
        )

    return MigrationInventory(
        root=root,
        migration_sets=tuple(migration_sets),
        files=tuple(files),
        findings=tuple(sorted(findings, key=_finding_sort_key)),
    )


def detect_unregistered_migration_directories(
    root: Path,
    *,
    registered_directories: Iterable[str],
    search_roots: Sequence[str] = ("src",),
) -> list[Finding]:
    """Find SQL-bearing ``Migrations`` directories absent from configuration."""

    root = root.resolve()
    registered = {(root / item).resolve() for item in registered_directories}
    findings: list[Finding] = []
    discovered: set[Path] = set()

    for search_root in search_roots:
        relative_search_root = _relative_directory(root, search_root)
        candidate_root = root / relative_search_root
        if not candidate_root.is_dir():
            continue
        for candidate in candidate_root.rglob("*"):
            if not candidate.is_dir() or candidate.name.casefold() != "migrations":
                continue
            if any(
                part.casefold() in _IGNORED_DIRECTORY_PARTS for part in candidate.parts
            ):
                continue
            if not any(path.is_file() for path in candidate.glob("*.sql")):
                continue
            discovered.add(candidate.resolve())

    for candidate in sorted(
        discovered - registered, key=lambda item: item.as_posix().casefold()
    ):
        relative = candidate.relative_to(root).as_posix()
        findings.append(
            Finding(
                "migration-directory-unregistered",
                "error",
                "SQL-bearing migration directory is not registered in schema-control configuration.",
                path=relative,
            )
        )
    return findings


def compare_immutable_migrations(
    inventory: MigrationInventory,
    read_base_file: BaseFileReader,
) -> list[Finding]:
    """Report immutable current files whose baseline content differs.

    A ``None`` baseline result means the file is newly added and is therefore not
    an immutable-history violation.
    """

    findings: list[Finding] = []
    for migration in inventory.files:
        if not migration.immutable:
            continue
        baseline = read_base_file(migration.path)
        if baseline is None:
            continue
        baseline_bytes = (
            baseline.encode("utf-8") if isinstance(baseline, str) else baseline
        )
        if sha256_bytes(baseline_bytes) == migration.sha256:
            continue
        findings.append(
            Finding(
                "migration-immutable-file-modified",
                "error",
                "Existing immutable migration differs from the comparison baseline; add a new migration instead.",
                path=migration.path,
                subject=migration.migration_set_id,
            )
        )
    return findings


def git_base_file_reader(root: Path, base_ref: str) -> BaseFileReader:
    """Create a baseline reader backed by ``git show <ref>:<path>``."""

    root = root.resolve()
    if not base_ref.strip():
        raise ValueError("base_ref cannot be empty.")
    verification = subprocess.run(
        ["git", "rev-parse", "--verify", f"{base_ref}^{{commit}}"],
        cwd=root,
        capture_output=True,
        check=False,
    )
    if verification.returncode != 0:
        details = verification.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"Unable to resolve git base ref '{base_ref}': {details}")

    baseline_listing = subprocess.run(
        ["git", "ls-tree", "-r", "--name-only", base_ref],
        cwd=root,
        capture_output=True,
        check=False,
    )
    if baseline_listing.returncode != 0:
        details = baseline_listing.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(
            f"Unable to list files at git base ref '{base_ref}': {details}"
        )
    baseline_paths = {
        line.replace("\\", "/")
        for line in baseline_listing.stdout.decode(
            "utf-8", errors="replace"
        ).splitlines()
        if line
    }
    changed_listing = subprocess.run(
        ["git", "diff", "--name-only", base_ref, "--"],
        cwd=root,
        capture_output=True,
        check=False,
    )
    if changed_listing.returncode != 0:
        details = changed_listing.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(
            f"Unable to compare files with git base ref '{base_ref}': {details}"
        )
    changed_paths = {
        line.replace("\\", "/")
        for line in changed_listing.stdout.decode(
            "utf-8", errors="replace"
        ).splitlines()
        if line
    }
    cache: dict[str, bytes | None] = {}

    def read(relative_path: str) -> bytes | None:
        normalized_path = relative_path.replace("\\", "/")
        if normalized_path in cache:
            return cache[normalized_path]
        if normalized_path not in baseline_paths:
            cache[normalized_path] = None
            return None
        current_path = root / normalized_path
        if normalized_path not in changed_paths and current_path.is_file():
            value = current_path.read_bytes()
            cache[normalized_path] = value
            return value
        result = subprocess.run(
            ["git", "show", f"{base_ref}:{normalized_path}"],
            cwd=root,
            capture_output=True,
            check=False,
        )
        value = result.stdout if result.returncode == 0 else None
        cache[normalized_path] = value
        return value

    return read


def _strip_sql_comments(sql: str) -> str:
    """Strip SQL comments while leaving quoted strings and identifiers intact."""

    output: list[str] = []
    index = 0
    state = "normal"
    dollar_tag = ""
    while index < len(sql):
        current = sql[index]
        following = sql[index + 1] if index + 1 < len(sql) else ""

        if state == "line-comment":
            if current == "\n":
                output.append(current)
                state = "normal"
            else:
                output.append(" ")
            index += 1
            continue
        if state == "block-comment":
            if current == "*" and following == "/":
                output.extend((" ", " "))
                index += 2
                state = "normal"
            else:
                output.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if state == "single-quote":
            output.append(current)
            if current == "'":
                if following == "'":
                    output.append(following)
                    index += 2
                    continue
                state = "normal"
            index += 1
            continue
        if state == "double-quote":
            output.append(current)
            if current == '"':
                if following == '"':
                    output.append(following)
                    index += 2
                    continue
                state = "normal"
            index += 1
            continue
        if state == "dollar-quote":
            if dollar_tag and sql.startswith(dollar_tag, index):
                output.extend(dollar_tag)
                index += len(dollar_tag)
                state = "normal"
            else:
                output.append(current)
                index += 1
            continue

        if current == "-" and following == "-":
            output.extend((" ", " "))
            index += 2
            state = "line-comment"
        elif current == "/" and following == "*":
            output.extend((" ", " "))
            index += 2
            state = "block-comment"
        elif current == "'":
            output.append(current)
            index += 1
            state = "single-quote"
        elif current == '"':
            output.append(current)
            index += 1
            state = "double-quote"
        elif current == "$":
            tag_match = re.match(r"\$[A-Za-z_][A-Za-z0-9_]*\$|\$\$", sql[index:])
            if tag_match is None:
                output.append(current)
                index += 1
            else:
                dollar_tag = tag_match.group(0)
                output.extend(dollar_tag)
                index += len(dollar_tag)
                state = "dollar-quote"
        else:
            output.append(current)
            index += 1
    return "".join(output)


def detect_destructive_changes(
    inventory: MigrationInventory,
    read_base_file: BaseFileReader,
    *,
    patterns: Sequence[DestructivePattern] = DEFAULT_DESTRUCTIVE_PATTERNS,
    severity: str = "error",
) -> list[Finding]:
    """Scan only newly added migrations for destructive PostgreSQL constructs."""

    findings: list[Finding] = []
    for migration in inventory.files:
        if read_base_file(migration.path) is not None:
            continue
        sql = (inventory.root / migration.path).read_text(encoding="utf-8")
        searchable = _strip_sql_comments(normalize_text(sql))
        for pattern in patterns:
            match = re.search(
                pattern.expression, searchable, flags=re.IGNORECASE | re.DOTALL
            )
            if match is None:
                continue
            line = searchable.count("\n", 0, match.start()) + 1
            findings.append(
                Finding(
                    pattern.rule_id,
                    severity,
                    f"{pattern.message} First match is on line {line}.",
                    path=migration.path,
                    subject=migration.migration_set_id,
                )
            )
    return sorted(findings, key=_finding_sort_key)


def _default_connector(database_url: str) -> Any:
    try:
        import psycopg  # type: ignore[import-not-found]
    except ImportError as exc:
        raise RuntimeError(
            "PostgreSQL migration application requires optional dependency 'psycopg'. "
            "Install the schema-control requirements or inject a database connector."
        ) from exc
    return psycopg.connect(database_url)


def _qualified_table(migration_set: MigrationSet) -> str:
    schema = migration_set.rendered_schema
    table = quote_identifier(migration_set.ledger.table)
    return f"{schema}.{table}"


def _assert_disposable_database(cursor: Any) -> None:
    """Refuse to apply schema-control migrations to a non-empty database."""

    cursor.execute(
        """
        /* schema-control disposable database guard */
        select object_kind, object_name
        from (
            select
                'schema'::text as object_kind,
                quote_ident(namespace.nspname) as object_name
            from pg_catalog.pg_namespace namespace
            where namespace.nspname <> 'public'
              and namespace.nspname <> 'information_schema'
              and namespace.nspname not like 'pg\\_%' escape '\\'

            union all

            select
                'relation'::text,
                pg_catalog.format('%I.%I', namespace.nspname, relation.relname)
            from pg_catalog.pg_class relation
            join pg_catalog.pg_namespace namespace
              on namespace.oid = relation.relnamespace
            where namespace.nspname = 'public'
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')

            union all

            select
                'routine'::text,
                routine.oid::pg_catalog.regprocedure::text
            from pg_catalog.pg_proc routine
            join pg_catalog.pg_namespace namespace
              on namespace.oid = routine.pronamespace
            where namespace.nspname = 'public'

            union all

            select
                'type'::text,
                pg_catalog.format('%I.%I', namespace.nspname, data_type.typname)
            from pg_catalog.pg_type data_type
            join pg_catalog.pg_namespace namespace
              on namespace.oid = data_type.typnamespace
            where namespace.nspname = 'public'
              and data_type.typtype in ('c', 'd', 'e', 'r')
        ) user_object
        order by object_kind, object_name
        limit 1;
        """
    )
    existing = cursor.fetchone()
    if existing is not None:
        kind, name = existing
        raise MigrationApplyError(
            "Schema control requires a disposable, empty PostgreSQL database; "
            f"found existing {kind} '{name}'."
        )


def _ensure_ledger(cursor: Any, migration_set: MigrationSet) -> None:
    key_column = quote_identifier(migration_set.ledger.key_column)
    checksum_column = quote_identifier(migration_set.ledger.checksum_column)
    ordinal_column = (
        f"{quote_identifier(migration_set.ledger.ordinal_column)} integer not null,"
        if migration_set.track_ordinals
        else ""
    )
    checksum_nullability = (
        "not null" if migration_set.ledger.checksum_required else "null"
    )
    cursor.execute(f"create schema if not exists {migration_set.rendered_schema};")
    cursor.execute(
        f"""
        create table if not exists {_qualified_table(migration_set)} (
            {ordinal_column}
            {key_column} text primary key,
            {checksum_column} text {checksum_nullability},
            applied_at timestamptz not null default now()
        );
        alter table {_qualified_table(migration_set)}
            add column if not exists {checksum_column} text;
        """
    )


def _applied_checksum(
    cursor: Any, migration_set: MigrationSet, filename: str
) -> str | None:
    cursor.execute(
        f"select {quote_identifier(migration_set.ledger.checksum_column)} "
        f"from {_qualified_table(migration_set)} "
        f"where {quote_identifier(migration_set.ledger.key_column)} = %s;",
        (filename,),
    )
    row = cursor.fetchone()
    return None if row is None else str(row[0])


def _record_migration(
    cursor: Any, migration_set: MigrationSet, migration: MigrationFile
) -> None:
    table = _qualified_table(migration_set)
    key_column = quote_identifier(migration_set.ledger.key_column)
    checksum_column = quote_identifier(migration_set.ledger.checksum_column)
    if migration_set.track_ordinals:
        ordinal_column = quote_identifier(migration_set.ledger.ordinal_column)
        cursor.execute(
            f"insert into {table} ({ordinal_column}, {key_column}, {checksum_column}) values (%s, %s, %s);",
            (migration.ordinal, migration.filename, migration.sha256),
        )
    else:
        cursor.execute(
            f"insert into {table} ({key_column}, {checksum_column}) values (%s, %s);",
            (migration.filename, migration.sha256),
        )


def apply_migrations(
    database_url: str,
    inventory: MigrationInventory,
    *,
    connect: DatabaseConnector | None = None,
) -> MigrationApplyResult:
    """Apply the inventory transactionally to a fresh PostgreSQL database.

    The database must contain no user schemas or objects. Matching ledger entries
    are still handled defensively, and checksum drift fails closed. ``psycopg`` is
    imported only when no connector is injected, keeping inventory and policy
    commands dependency-free.
    """

    if not database_url.strip():
        raise ValueError("database_url cannot be empty.")
    if inventory.has_errors:
        details = "; ".join(
            f"{finding.rule_id}: {finding.message}"
            for finding in inventory.findings
            if finding.severity == "error"
        )
        raise MigrationApplyError(f"Migration inventory contains errors: {details}")

    migration_set_ids = {item.id for item in inventory.migration_sets}
    unknown_set_files = [
        item.path
        for item in inventory.files
        if item.migration_set_id not in migration_set_ids
    ]
    if unknown_set_files:
        raise MigrationApplyError(
            "Migration inventory references unknown migration sets: "
            + ", ".join(sorted(unknown_set_files))
        )

    connector = connect or _default_connector
    connection = connector(database_url)
    cursor = None
    applied: list[str] = []
    skipped: list[str] = []
    try:
        cursor = connection.cursor()
        _assert_disposable_database(cursor)
        for migration_set in inventory.migration_sets:
            _ensure_ledger(cursor, migration_set)
            for migration in inventory.files_for(migration_set.id):
                previous_checksum = _applied_checksum(
                    cursor, migration_set, migration.filename
                )
                if previous_checksum is not None:
                    if previous_checksum != migration.sha256:
                        raise MigrationApplyError(
                            f"Applied migration '{migration.path}' differs from its configured ledger checksum."
                        )
                    skipped.append(migration.path)
                    continue

                sql = (inventory.root / migration.path).read_text(encoding="utf-8")
                rendered_sql = normalize_text(sql).replace(
                    "__SCHEMA__", migration_set.rendered_schema
                )
                cursor.execute(rendered_sql)
                _record_migration(cursor, migration_set, migration)
                applied.append(migration.path)
        connection.commit()
    except Exception:
        if hasattr(connection, "rollback"):
            connection.rollback()
        raise
    finally:
        if cursor is not None and hasattr(cursor, "close"):
            cursor.close()
        if hasattr(connection, "close"):
            connection.close()

    return MigrationApplyResult(tuple(applied), tuple(skipped))
