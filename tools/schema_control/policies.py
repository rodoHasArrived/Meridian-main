"""Deterministic policy evaluation for extracted PostgreSQL catalogs.

The evaluator intentionally operates on JSON-compatible dictionaries.  It does
not connect to PostgreSQL and accepts both the aggregate catalog shape emitted by
schema-control and compact, inline manifests used by tests and downstream tools.
"""

from __future__ import annotations

from collections.abc import Iterable, Mapping, Sequence
from fnmatch import fnmatchcase
from typing import Any

from .common import canonical_json, fingerprint


_SEVERITY_ORDER = {"error": 0, "warning": 1, "info": 2}
_RULE_DEFAULT_SEVERITIES = {
    "table-must-have-primary-key": "error",
    "foreign-key-must-be-indexed": "error",
    "business-table-must-have-comment": "warning",
    "schema-rls-required": "error",
    "no-public-schema": "error",
    "legacy-reapply-migration": "warning",
}
_OBJECT_COLLECTIONS = (
    "tables",
    "views",
    "materialized_views",
    "functions",
    "procedures",
    "sequences",
    "enums",
    "domains",
    "composite_types",
    "types",
)
_IDENTITY_KEYS = frozenset(
    {
        "name",
        "schema",
        "schema_name",
        "table",
        "table_name",
        "qualified_name",
    }
)


def evaluate_policies(
    catalog: dict[str, Any],
    migration_manifest: dict[str, Any],
    policies: dict[str, Any],
) -> dict[str, Any]:
    """Evaluate configured schema policies and return a stable report.

    Rules are enabled by adding them beneath ``policies["rules"]``. A rule may
    be configured with ``severity``, ``enabled``, and ``exclude`` (glob patterns
    matched against qualified objects, schemas, and local names). Existing
    migration findings are always carried forward and retain their severity.
    """

    if not isinstance(catalog, Mapping):
        raise TypeError("catalog must be a mapping")
    if not isinstance(migration_manifest, Mapping):
        raise TypeError("migration_manifest must be a mapping")
    if not isinstance(policies, Mapping):
        raise TypeError("policies must be a mapping")

    findings = _existing_migration_findings(migration_manifest)
    tables = _collect_tables(catalog)
    rules = _configured_rules(policies)

    config = rules.get("table-must-have-primary-key")
    if config is not None:
        severity = _severity("table-must-have-primary-key", config)
        for table in tables:
            if _excluded(config, *_table_candidates(table)) or _has_primary_key(
                catalog, table
            ):
                continue
            qualified = _qualified_table(table)
            findings.append(
                _finding(
                    "table-must-have-primary-key",
                    severity,
                    qualified,
                    f"Table {qualified} does not have a primary key.",
                    schema=table["schema"],
                    table=table["name"],
                )
            )

    config = rules.get("foreign-key-must-be-indexed")
    if config is not None:
        severity = _severity("foreign-key-must-be-indexed", config)
        for table in tables:
            qualified = _qualified_table(table)
            index_prefixes = _index_column_sets(catalog, table)
            for foreign_key in _foreign_keys(catalog, table):
                columns = _column_names(
                    foreign_key.get(
                        "columns",
                        foreign_key.get(
                            "source_columns",
                            foreign_key.get(
                                "constrained_columns",
                                foreign_key.get("local_columns", []),
                            ),
                        ),
                    )
                )
                if not columns:
                    source = foreign_key.get("source")
                    if isinstance(source, Mapping):
                        columns = _column_names(source.get("columns", []))
                if not columns:
                    continue
                constraint = _entry_name(foreign_key) or "unnamed-foreign-key"
                foreign_key_object = f"{qualified}.{constraint}"
                if _excluded(
                    config,
                    foreign_key_object,
                    qualified,
                    table["schema"],
                    table["name"],
                    constraint,
                ):
                    continue
                if any(prefix[: len(columns)] == columns for prefix in index_prefixes):
                    continue
                findings.append(
                    _finding(
                        "foreign-key-must-be-indexed",
                        severity,
                        foreign_key_object,
                        f"Foreign key {foreign_key_object} is not covered by a left-prefix index.",
                        schema=table["schema"],
                        table=table["name"],
                        constraint=constraint,
                        columns=columns,
                    )
                )

    config = rules.get("business-table-must-have-comment")
    if config is not None:
        severity = _severity("business-table-must-have-comment", config)
        for table in tables:
            if _excluded(config, *_table_candidates(table)) or _table_has_comment(
                catalog, table
            ):
                continue
            qualified = _qualified_table(table)
            findings.append(
                _finding(
                    "business-table-must-have-comment",
                    severity,
                    qualified,
                    f"Business table {qualified} does not have a table comment.",
                    schema=table["schema"],
                    table=table["name"],
                )
            )

    config = rules.get("schema-rls-required")
    if config is not None:
        severity = _severity("schema-rls-required", config)
        included_schemas = _string_list(config.get("include_schemas", ["*"]))
        if not included_schemas:
            included_schemas = ["*"]
        for table in tables:
            schema = table["schema"]
            if not any(fnmatchcase(schema, pattern) for pattern in included_schemas):
                continue
            if _excluded(config, *_table_candidates(table)) or _rls_enabled(
                catalog, table
            ):
                continue
            qualified = _qualified_table(table)
            findings.append(
                _finding(
                    "schema-rls-required",
                    severity,
                    qualified,
                    f"Table {qualified} is in an RLS-required schema but row-level security is disabled.",
                    schema=schema,
                    table=table["name"],
                )
            )

    config = rules.get("no-public-schema")
    if config is not None:
        severity = _severity("no-public-schema", config)
        for object_name in _public_objects(catalog, tables):
            local_name = object_name.removeprefix("public.")
            if _excluded(config, object_name, "public", local_name):
                continue
            findings.append(
                _finding(
                    "no-public-schema",
                    severity,
                    object_name,
                    f"Application object {object_name} uses the public schema.",
                    schema="public",
                )
            )

    config = rules.get("legacy-reapply-migration")
    if config is not None:
        severity = _severity("legacy-reapply-migration", config)
        for migration_set in _migration_sets(migration_manifest):
            drift_policy = str(migration_set.get("drift_policy", "")).strip().casefold()
            legacy_reapply = migration_set.get("legacy_reapply") is True
            if drift_policy != "reapply" and not legacy_reapply:
                continue
            object_name = str(
                migration_set.get("id")
                or migration_set.get("directory")
                or migration_set.get("schema")
                or "migration-set"
            )
            candidates = (
                object_name,
                str(migration_set.get("directory", "")),
                str(migration_set.get("schema", "")),
            )
            if _excluded(config, *candidates):
                continue
            findings.append(
                _finding(
                    "legacy-reapply-migration",
                    severity,
                    object_name,
                    f"Migration set {object_name} uses legacy reapply-on-drift behavior.",
                    migration_set=object_name,
                    schema=migration_set.get("schema"),
                    directory=migration_set.get("directory"),
                    drift_policy=migration_set.get("drift_policy", "reapply"),
                )
            )

    sorted_findings = _sort_and_deduplicate(findings)
    counts: dict[str, int] = {
        "error": 0,
        "warning": 0,
        "info": 0,
        "total": len(sorted_findings),
    }
    for finding_item in sorted_findings:
        severity_key = str(finding_item["severity"]).casefold()
        counts[severity_key] = counts.get(severity_key, 0) + 1
    failed = counts["error"] > 0
    report: dict[str, Any] = {
        "findings": sorted_findings,
        "counts": counts,
        "failed": failed,
    }
    report["fingerprint"] = fingerprint(report)
    return report


def _configured_rules(policies: Mapping[str, Any]) -> dict[str, Mapping[str, Any]]:
    raw_rules = policies.get("rules", policies)
    if not isinstance(raw_rules, Mapping):
        raise ValueError("policies.rules must be a mapping")
    unknown_rule_ids = sorted(set(raw_rules) - set(_RULE_DEFAULT_SEVERITIES))
    if unknown_rule_ids:
        raise ValueError(
            "Unknown schema-control policy rule id(s): "
            + ", ".join(str(item) for item in unknown_rule_ids)
        )
    result: dict[str, Mapping[str, Any]] = {}
    for rule_id in _RULE_DEFAULT_SEVERITIES:
        if rule_id not in raw_rules:
            continue
        raw_config = raw_rules[rule_id]
        if raw_config is False or raw_config is None:
            continue
        if raw_config is True:
            config: Mapping[str, Any] = {}
        elif isinstance(raw_config, str):
            config = {"severity": raw_config}
        elif isinstance(raw_config, Mapping):
            config = raw_config
        else:
            raise ValueError(
                f"Policy rule '{rule_id}' must be a mapping, severity string, or boolean."
            )
        if config.get("enabled", True) is False:
            continue
        result[rule_id] = config
    return result


def _severity(rule_id: str, config: Mapping[str, Any]) -> str:
    severity = (
        str(config.get("severity", _RULE_DEFAULT_SEVERITIES[rule_id]))
        .strip()
        .casefold()
    )
    if severity not in _SEVERITY_ORDER:
        raise ValueError(
            f"Policy rule '{rule_id}' has unsupported severity '{severity}'; "
            "expected error, warning, or info."
        )
    return severity


def _excluded(config: Mapping[str, Any], *candidates: str) -> bool:
    raw_patterns = config.get("exclude", config.get("exclusions", []))
    patterns: list[str] = []
    if isinstance(raw_patterns, Mapping):
        for value in raw_patterns.values():
            patterns.extend(_string_list(value))
    else:
        patterns.extend(_string_list(raw_patterns))
    return any(
        candidate and fnmatchcase(candidate, pattern)
        for candidate in candidates
        for pattern in patterns
    )


def _string_list(value: Any) -> list[str]:
    if value is None:
        return []
    if isinstance(value, str):
        return [value]
    if isinstance(value, Sequence) and not isinstance(value, (bytes, bytearray)):
        return [str(item) for item in value if str(item)]
    return [str(value)]


def _entries(value: Any) -> list[dict[str, Any]]:
    if isinstance(value, Mapping):
        if _IDENTITY_KEYS.intersection(value):
            return [dict(value)]
        result: list[dict[str, Any]] = []
        for name, item in value.items():
            if not isinstance(item, Mapping):
                continue
            entry = dict(item)
            entry.setdefault("name", str(name))
            result.append(entry)
        return result
    if isinstance(value, Sequence) and not isinstance(value, (str, bytes, bytearray)):
        return [dict(item) for item in value if isinstance(item, Mapping)]
    return []


def _schema_entries(catalog: Mapping[str, Any]) -> list[dict[str, Any]]:
    entries = _entries(catalog.get("schemas", []))
    raw_schemas = catalog.get("schemas", [])
    sequence_schemas = (
        raw_schemas
        if isinstance(raw_schemas, Sequence)
        and not isinstance(raw_schemas, (str, bytes, bytearray))
        else []
    )
    for item in sequence_schemas:
        if isinstance(item, str):
            entries.append({"name": item})
    if isinstance(catalog.get("schema"), str) and any(
        key in catalog for key in _OBJECT_COLLECTIONS
    ):
        entries.append(dict(catalog))
    return entries


def _collect_tables(catalog: Mapping[str, Any]) -> list[dict[str, Any]]:
    collected: dict[tuple[str, str], dict[str, Any]] = {}

    def add(raw_table: Mapping[str, Any], default_schema: str = "") -> None:
        schema, name = _table_identity(raw_table, default_schema)
        if not name:
            return
        key = (schema, name)
        if key not in collected:
            collected[key] = {"schema": schema, "name": name, "sources": []}
        collected[key]["sources"].append(raw_table)

    for schema_entry in _schema_entries(catalog):
        schema_name = _schema_name(schema_entry)
        for table in _entries(schema_entry.get("tables", [])):
            add(table, schema_name)
        for relation in _entries(schema_entry.get("relations", [])):
            if _is_table_relation(relation):
                add(relation, schema_name)
        objects = schema_entry.get("objects")
        if isinstance(objects, Mapping):
            for table in _entries(objects.get("tables", [])):
                add(table, schema_name)

    default_schema = str(catalog.get("schema", catalog.get("schema_name", "")))
    for table in _entries(catalog.get("tables", [])):
        add(table, default_schema)
    for relation in _entries(catalog.get("relations", [])):
        if _is_table_relation(relation):
            add(relation, default_schema)

    return [collected[key] for key in sorted(collected)]


def _is_table_relation(relation: Mapping[str, Any]) -> bool:
    kind = str(
        relation.get(
            "kind", relation.get("relation_kind", relation.get("relkind", "table"))
        )
    )
    normalized = kind.strip().casefold().replace("-", "_").replace(" ", "_")
    return normalized in {"r", "p", "table", "base_table", "partitioned_table"}


def _schema_name(entry: Mapping[str, Any]) -> str:
    return str(entry.get("name", entry.get("schema", entry.get("schema_name", ""))))


def _table_identity(
    entry: Mapping[str, Any], default_schema: str = ""
) -> tuple[str, str]:
    schema = str(
        entry.get(
            "schema",
            entry.get("schema_name", entry.get("table_schema", default_schema)),
        )
    )
    name = str(
        entry.get("name", entry.get("table_name", entry.get("relation_name", "")))
    )
    qualified = str(entry.get("qualified_name", ""))
    if not name and qualified:
        name = qualified
    if "." in name and not schema:
        schema, name = name.split(".", 1)
    elif qualified and "." in qualified and not schema:
        schema, name = qualified.split(".", 1)
    return schema, name


def _qualified_table(table: Mapping[str, Any]) -> str:
    schema = str(table["schema"])
    return f"{schema}.{table['name']}" if schema else str(table["name"])


def _table_candidates(table: Mapping[str, Any]) -> tuple[str, str, str]:
    return _qualified_table(table), str(table["schema"]), str(table["name"])


def _nested_entries(table: Mapping[str, Any], collection: str) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for source in table["sources"]:
        result.extend(_entries(source.get(collection, [])))
    return result


def _belongs_to_table(entry: Mapping[str, Any], table: Mapping[str, Any]) -> bool:
    source = entry.get("source")
    if isinstance(source, Mapping):
        entry = {**entry, **{f"source_{key}": value for key, value in source.items()}}
    entry_schema = str(
        entry.get(
            "table_schema",
            entry.get(
                "schema", entry.get("schema_name", entry.get("source_schema", ""))
            ),
        )
    )
    entry_table = str(
        entry.get(
            "table_name",
            entry.get(
                "table", entry.get("relation_name", entry.get("source_table", ""))
            ),
        )
    )
    if "." in entry_table and not entry_schema:
        entry_schema, entry_table = entry_table.split(".", 1)
    if not entry_table:
        return False
    return entry_table == table["name"] and (
        not entry_schema or entry_schema == table["schema"]
    )


def _related_entries(
    catalog: Mapping[str, Any],
    table: Mapping[str, Any],
    collection: str,
) -> list[dict[str, Any]]:
    nested = _nested_entries(table, collection)
    global_entries = [
        item
        for item in _entries(catalog.get(collection, []))
        if _belongs_to_table(item, table)
    ]
    return nested + global_entries


def _constraint_kind(entry: Mapping[str, Any]) -> str:
    raw = str(entry.get("constraint_type", entry.get("type", entry.get("kind", ""))))
    return raw.strip().casefold().replace("-", "_").replace(" ", "_")


def _has_primary_key(catalog: Mapping[str, Any], table: Mapping[str, Any]) -> bool:
    for source in table["sources"]:
        if source.get("has_primary_key") is True:
            return True
        if _column_names(source.get("primary_key_columns", [])):
            return True
        primary_key = source.get("primary_key")
        if primary_key is True or isinstance(primary_key, str) and primary_key.strip():
            return True
        if isinstance(primary_key, Mapping):
            if primary_key.get("present") is True or primary_key.get("enabled") is True:
                return True
            if _column_names(
                primary_key.get("columns", primary_key.get("key_columns", []))
            ):
                return True
        elif isinstance(primary_key, Sequence) and not isinstance(
            primary_key, (str, bytes, bytearray)
        ):
            if len(primary_key) > 0:
                return True
        if _entries(source.get("primary_keys", [])):
            return True
    return any(
        _constraint_kind(item) in {"p", "primary", "primary_key", "primarykey"}
        for item in _related_entries(catalog, table, "constraints")
    )


def _foreign_keys(
    catalog: Mapping[str, Any], table: Mapping[str, Any]
) -> list[dict[str, Any]]:
    result = _related_entries(catalog, table, "foreign_keys")
    result.extend(
        item
        for item in _related_entries(catalog, table, "constraints")
        if _constraint_kind(item) in {"f", "foreign", "foreign_key", "foreignkey"}
    )
    return _deduplicate_entries(result)


def _index_column_sets(
    catalog: Mapping[str, Any], table: Mapping[str, Any]
) -> list[list[str]]:
    result: list[list[str]] = []
    for index in _related_entries(catalog, table, "indexes"):
        if index.get("valid") is False or index.get("ready") is False:
            continue
        if (
            index.get("is_partial") is True
            or index.get("predicate")
            or index.get("where")
        ):
            continue
        columns = _column_names(
            index.get(
                "key_columns",
                index.get("columns", index.get("index_columns", index.get("keys", []))),
            )
        )
        if columns:
            result.append(columns)

    for source in table["sources"]:
        primary_key = source.get("primary_key")
        if isinstance(primary_key, Mapping):
            columns = _column_names(
                primary_key.get("columns", primary_key.get("key_columns", []))
            )
            if columns:
                result.append(columns)
        columns = _column_names(source.get("primary_key_columns", []))
        if columns:
            result.append(columns)

    for constraint in _related_entries(catalog, table, "constraints"):
        if _constraint_kind(constraint) not in {
            "p",
            "primary",
            "primary_key",
            "primarykey",
            "u",
            "unique",
        }:
            continue
        columns = _column_names(
            constraint.get("columns", constraint.get("key_columns", []))
        )
        if columns:
            result.append(columns)
    return _deduplicate_lists(result)


def _column_names(value: Any) -> list[str]:
    if isinstance(value, str):
        return [value] if value else []
    if isinstance(value, Mapping):
        if any(key in value for key in ("name", "column", "column_name", "attname")):
            value = [value]
        else:
            return [str(key) for key in value]
    if not isinstance(value, Sequence) or isinstance(value, (bytes, bytearray)):
        return []
    result: list[str] = []
    for item in value:
        if isinstance(item, str):
            if item:
                result.append(item)
            continue
        if not isinstance(item, Mapping):
            continue
        if item.get("included") is True or item.get("is_included") is True:
            continue
        name = item.get(
            "name", item.get("column", item.get("column_name", item.get("attname")))
        )
        if name is not None and str(name):
            result.append(str(name))
    return result


def _table_has_comment(catalog: Mapping[str, Any], table: Mapping[str, Any]) -> bool:
    for source in table["sources"]:
        for key in ("comment", "description", "table_comment"):
            value = source.get(key)
            if isinstance(value, str) and value.strip():
                return True
    for comment in _related_entries(catalog, table, "comments"):
        object_type = str(
            comment.get("object_type", comment.get("kind", "table"))
        ).casefold()
        if object_type not in {"table", "relation", "r", "p"}:
            continue
        value = comment.get(
            "comment", comment.get("description", comment.get("text", ""))
        )
        if isinstance(value, str) and value.strip():
            return True
    return False


def _rls_enabled(catalog: Mapping[str, Any], table: Mapping[str, Any]) -> bool:
    for source in table["sources"]:
        for key in (
            "rls_enabled",
            "row_level_security_enabled",
            "row_security_enabled",
        ):
            if source.get(key) is True:
                return True
        rls = source.get("rls", source.get("row_level_security"))
        if rls is True:
            return True
        if isinstance(rls, Mapping) and rls.get("enabled") is True:
            return True
    for rls in _related_entries(catalog, table, "row_level_security"):
        if rls.get("enabled") is True:
            return True
    return False


def _public_objects(
    catalog: Mapping[str, Any],
    tables: Sequence[Mapping[str, Any]],
) -> list[str]:
    result = {
        _qualified_table(table) for table in tables if table["schema"] == "public"
    }

    for schema_entry in _schema_entries(catalog):
        if _schema_name(schema_entry) != "public":
            continue
        for relation in _entries(schema_entry.get("relations", [])):
            name = _entry_name(relation) or "relation"
            result.add(f"public.{name}")
        for collection in _OBJECT_COLLECTIONS:
            if collection == "tables":
                continue
            for item in _entries(schema_entry.get(collection, [])):
                name = _entry_name(item) or collection
                result.add(f"public.{name}")
        objects = schema_entry.get("objects")
        if isinstance(objects, Mapping):
            for collection in _OBJECT_COLLECTIONS:
                if collection == "tables":
                    continue
                for item in _entries(objects.get(collection, [])):
                    name = _entry_name(item) or collection
                    result.add(f"public.{name}")
        object_count = schema_entry.get("object_count")
        if isinstance(object_count, int) and object_count > 0 and not result:
            result.add("public")

    for collection in _OBJECT_COLLECTIONS:
        if collection == "tables":
            continue
        for item in _entries(catalog.get(collection, [])):
            schema = str(item.get("schema", item.get("schema_name", "")))
            if schema != "public":
                continue
            name = _entry_name(item) or collection
            result.add(f"public.{name}")
    return sorted(result)


def _entry_name(entry: Mapping[str, Any]) -> str:
    return str(
        entry.get(
            "name",
            entry.get(
                "constraint_name",
                entry.get(
                    "index_name", entry.get("function_name", entry.get("signature", ""))
                ),
            ),
        )
    )


def _migration_sets(manifest: Mapping[str, Any]) -> list[dict[str, Any]]:
    result = _entries(manifest.get("migration_sets", manifest.get("migrations", [])))
    for container_name in (
        "configuration",
        "config",
        "inventory",
        "migration_inventory",
    ):
        container = manifest.get(container_name)
        if isinstance(container, Mapping):
            result.extend(
                _entries(
                    container.get("migration_sets", container.get("migrations", []))
                )
            )
    return _deduplicate_entries(result)


def _existing_migration_findings(manifest: Mapping[str, Any]) -> list[dict[str, Any]]:
    raw_findings: list[Any] = []
    for key in ("findings", "migration_findings"):
        value = manifest.get(key, [])
        if isinstance(value, Sequence) and not isinstance(
            value, (str, bytes, bytearray)
        ):
            raw_findings.extend(value)
    for container_name in ("inventory", "migration_inventory"):
        container = manifest.get(container_name)
        if not isinstance(container, Mapping):
            continue
        value = container.get("findings", [])
        if isinstance(value, Sequence) and not isinstance(
            value, (str, bytes, bytearray)
        ):
            raw_findings.extend(value)

    result: list[dict[str, Any]] = []
    for raw in raw_findings:
        if not isinstance(raw, Mapping):
            continue
        rule_id = str(raw.get("rule_id", raw.get("code", "migration-finding")))
        severity = str(raw.get("severity", "error"))
        object_name = str(
            raw.get("object", raw.get("path", raw.get("subject", "migration")))
        )
        message = str(raw.get("message", rule_id))
        raw_details = raw.get("details", {})
        details = (
            dict(raw_details)
            if isinstance(raw_details, Mapping)
            else {"value": raw_details}
        )
        for key, value in raw.items():
            if key not in {
                "rule_id",
                "code",
                "severity",
                "object",
                "message",
                "details",
            }:
                details.setdefault(str(key), value)
        result.append(
            {
                "rule_id": rule_id,
                "severity": severity,
                "object": object_name,
                "message": message,
                "details": details,
            }
        )
    return result


def _finding(
    rule_id: str,
    severity: str,
    object_name: str,
    message: str,
    **details: Any,
) -> dict[str, Any]:
    return {
        "rule_id": rule_id,
        "severity": severity,
        "object": object_name,
        "message": message,
        "details": {key: value for key, value in details.items() if value is not None},
    }


def _sort_and_deduplicate(findings: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    unique: dict[str, dict[str, Any]] = {}
    for finding_item in findings:
        normalized = {
            "rule_id": str(finding_item["rule_id"]),
            "severity": str(finding_item["severity"]),
            "object": str(finding_item["object"]),
            "message": str(finding_item["message"]),
            "details": dict(finding_item.get("details", {})),
        }
        unique[canonical_json(normalized, indent=None, trailing_newline=False)] = (
            normalized
        )

    def sort_key(item: dict[str, Any]) -> tuple[Any, ...]:
        severity = str(item["severity"]).casefold()
        return (
            _SEVERITY_ORDER.get(severity, len(_SEVERITY_ORDER)),
            severity,
            item["rule_id"],
            item["object"],
            item["message"],
            canonical_json(item["details"], indent=None, trailing_newline=False),
        )

    return sorted(unique.values(), key=sort_key)


def _deduplicate_entries(entries: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    unique: dict[str, dict[str, Any]] = {}
    for entry in entries:
        unique[canonical_json(entry, indent=None, trailing_newline=False)] = entry
    return [unique[key] for key in sorted(unique)]


def _deduplicate_lists(values: Iterable[list[str]]) -> list[list[str]]:
    unique = {tuple(value) for value in values}
    return [list(value) for value in sorted(unique)]
