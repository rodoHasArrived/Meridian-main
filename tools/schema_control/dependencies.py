"""Compose database, contract, and explicit logical dependency graphs."""

from __future__ import annotations

from collections.abc import Mapping
from typing import Any

from .common import fingerprint, normalize_text


def _schema_items(catalog: Mapping[str, Any]) -> list[Mapping[str, Any]]:
    schemas = catalog.get("schemas", [])
    if isinstance(schemas, Mapping):
        return [
            {"name": name, **(value if isinstance(value, Mapping) else {})}
            for name, value in schemas.items()
        ]
    return [item for item in schemas if isinstance(item, Mapping)]


def _relation_items(schema: Mapping[str, Any]) -> list[Mapping[str, Any]]:
    relations = schema.get("relations")
    if isinstance(relations, list):
        return [item for item in relations if isinstance(item, Mapping)]
    result: list[Mapping[str, Any]] = []
    for key in ("tables", "views", "materialized_views", "sequences", "foreign_tables"):
        values = schema.get(key, [])
        if isinstance(values, list):
            result.extend(item for item in values if isinstance(item, Mapping))
    return result


def _type_items(contracts: Mapping[str, Any]) -> list[Mapping[str, Any]]:
    for key in ("types", "objects", "contracts"):
        values = contracts.get(key)
        if isinstance(values, list):
            return [item for item in values if isinstance(item, Mapping)]
        if isinstance(values, Mapping):
            return [
                {"full_name": name, **(value if isinstance(value, Mapping) else {})}
                for name, value in values.items()
            ]
    partitions = contracts.get("partitions", {})
    if isinstance(partitions, Mapping):
        result: list[Mapping[str, Any]] = []
        for partition in partitions.values():
            if isinstance(partition, Mapping):
                values = partition.get("types", [])
                if isinstance(values, list):
                    result.extend(item for item in values if isinstance(item, Mapping))
        return result
    return []


def _contract_set_ids(item: Mapping[str, Any]) -> set[str]:
    raw = item.get("contract_sets") or item.get("sets") or item.get("modules") or []
    if isinstance(raw, str):
        return {raw}
    return {str(value) for value in raw}


def build_dependency_manifest(
    catalog: Mapping[str, Any],
    contracts: Mapping[str, Any],
    config: Mapping[str, Any],
) -> dict[str, Any]:
    """Return one deterministic graph without claiming DTO/table equivalence.

    Database foreign keys and contract type references are discovered edges. Cross-schema
    references and database-to-contract associations are emitted only from the explicit
    registry in ``config``.
    """

    nodes: dict[str, dict[str, Any]] = {}
    edges: dict[tuple[str, str, str, str], dict[str, Any]] = {}

    def add_node(key: str, kind: str, label: str, **metadata: Any) -> None:
        nodes.setdefault(
            key,
            {
                "id": key,
                "kind": kind,
                "label": normalize_text(label),
                **metadata,
            },
        )

    def add_edge(
        source: str, target: str, kind: str, label: str = "", **metadata: Any
    ) -> None:
        edge_key = (source, target, kind, normalize_text(label))
        edges.setdefault(
            edge_key,
            {
                "source": source,
                "target": target,
                "kind": kind,
                "label": normalize_text(label),
                **metadata,
            },
        )

    for schema in _schema_items(catalog):
        schema_name = str(schema.get("name") or schema.get("schema") or "").strip()
        if not schema_name:
            continue
        schema_key = f"schema:{schema_name}"
        add_node(schema_key, "database-schema", schema_name)
        for relation in _relation_items(schema):
            relation_name = str(
                relation.get("name") or relation.get("relation_name") or ""
            ).strip()
            if not relation_name:
                continue
            relation_schema = str(relation.get("schema") or schema_name)
            relation_key = f"database:{relation_schema}.{relation_name}"
            relation_kind = str(
                relation.get("kind") or relation.get("relation_kind") or "table"
            )
            add_node(
                relation_key,
                f"database-{relation_kind}",
                f"{relation_schema}.{relation_name}",
                schema=relation_schema,
            )
            add_edge(schema_key, relation_key, "contains")

            for constraint in relation.get("constraints", []) or []:
                if not isinstance(constraint, Mapping):
                    continue
                constraint_type = str(
                    constraint.get("type") or constraint.get("kind") or ""
                ).lower()
                if constraint_type not in {
                    "f",
                    "foreign",
                    "foreign_key",
                    "foreign-key",
                }:
                    continue
                target_schema = str(
                    constraint.get("referenced_schema")
                    or constraint.get("target_schema")
                    or relation_schema
                )
                target_name = str(
                    constraint.get("referenced_table")
                    or constraint.get("target_table")
                    or constraint.get("referenced_relation")
                    or ""
                )
                if not target_name:
                    continue
                target_key = f"database:{target_schema}.{target_name}"
                add_node(
                    target_key,
                    "database-table",
                    f"{target_schema}.{target_name}",
                    schema=target_schema,
                )
                columns = (
                    constraint.get("columns") or constraint.get("source_columns") or []
                )
                add_edge(
                    relation_key,
                    target_key,
                    "foreign-key",
                    ", ".join(str(item) for item in columns),
                    constraint=str(constraint.get("name") or ""),
                )

    for dependency in catalog.get("dependencies", []) or []:
        if not isinstance(dependency, Mapping):
            continue
        source = str(dependency.get("source") or "")
        target = str(dependency.get("target") or "")
        if source and target:
            source_key = source if ":" in source else f"database:{source}"
            target_key = target if ":" in target else f"database:{target}"
            add_node(source_key, "database-object", source)
            add_node(target_key, "database-object", target)
            add_edge(
                source_key,
                target_key,
                str(dependency.get("kind") or "catalog-dependency"),
            )

    known_contracts: set[str] = set()
    contract_items = _type_items(contracts)
    for item in contract_items:
        full_name = str(
            item.get("full_name") or item.get("key") or item.get("name") or ""
        ).strip()
        if not full_name:
            continue
        known_contracts.add(full_name)
        add_node(
            f"contract:{full_name}",
            "contract-type",
            full_name,
            classification=str(
                item.get("classification") or item.get("kind") or "contract"
            ),
        )

    for item in contract_items:
        full_name = str(
            item.get("full_name") or item.get("key") or item.get("name") or ""
        ).strip()
        if not full_name:
            continue
        for reference in item.get("references", []) or []:
            target = str(
                reference.get("type") if isinstance(reference, Mapping) else reference
            )
            if target in known_contracts and target != full_name:
                add_edge(
                    f"contract:{full_name}", f"contract:{target}", "contract-reference"
                )

    for contract_set in config.get("contract_sets", []) or []:
        if not isinstance(contract_set, Mapping):
            continue
        set_id = str(contract_set.get("id") or "").strip()
        if not set_id:
            continue
        set_key = f"contract-set:{set_id}"
        add_node(set_key, "contract-set", set_id)
        for schema_name in contract_set.get("schemas", []) or []:
            schema_key = f"schema:{schema_name}"
            add_node(schema_key, "database-schema", str(schema_name))
            add_edge(
                schema_key,
                set_key,
                "module-contract-map",
                "explicit module mapping",
                structural_equivalence=False,
            )
        for item in contract_items:
            full_name = str(
                item.get("full_name") or item.get("key") or item.get("name") or ""
            ).strip()
            if full_name and set_id in _contract_set_ids(item):
                add_edge(
                    set_key,
                    f"contract:{full_name}",
                    "contract-set-member",
                )

    module_ids: set[str] = set()
    for migration_set in config.get("migration_sets", []) or []:
        if not isinstance(migration_set, Mapping):
            continue
        module_id = str(migration_set.get("id") or "").strip()
        schema_name = str(migration_set.get("schema") or "").strip()
        if not module_id:
            continue
        module_ids.add(module_id)
        module_key = f"migration-module:{module_id}"
        add_node(module_key, "migration-module", module_id)
        if schema_name:
            schema_key = f"schema:{schema_name}"
            add_node(schema_key, "database-schema", schema_name)
            add_edge(schema_key, module_key, "hosts-migration-module")

    def resolve_logical_reference(value: str) -> str:
        if value.endswith(".*"):
            schema_key = f"schema:{value[:-2]}"
            if schema_key in nodes:
                return schema_key
        database_key = f"database:{value}"
        if database_key in nodes:
            return database_key
        module_name = value.removesuffix(" module")
        if module_name in module_ids:
            return f"migration-module:{module_name}"
        logical_key = f"logical:{value}"
        add_node(logical_key, "logical-data-object", value)
        return logical_key

    for dependency in config.get("logical_dependencies", []) or []:
        if not isinstance(dependency, Mapping):
            continue
        source = str(dependency.get("from") or "").strip()
        target = str(dependency.get("to") or "").strip()
        if not source or not target:
            continue
        source_key = resolve_logical_reference(source)
        target_key = resolve_logical_reference(target)
        add_edge(
            source_key,
            target_key,
            "logical-reference",
            str(dependency.get("label") or ""),
            declared=True,
        )

    ordered_nodes = sorted(nodes.values(), key=lambda item: str(item["id"]))
    ordered_edges = sorted(
        edges.values(),
        key=lambda item: (
            str(item["source"]),
            str(item["target"]),
            str(item["kind"]),
            str(item["label"]),
        ),
    )
    result: dict[str, Any] = {
        "format": "meridian.schema-dependencies.v1",
        "nodes": ordered_nodes,
        "edges": ordered_edges,
        "summary": {
            "nodes": len(ordered_nodes),
            "edges": len(ordered_edges),
            "foreign_keys": sum(
                item["kind"] == "foreign-key" for item in ordered_edges
            ),
            "contract_references": sum(
                item["kind"] == "contract-reference" for item in ordered_edges
            ),
            "contract_set_memberships": sum(
                item["kind"] == "contract-set-member" for item in ordered_edges
            ),
            "logical_references": sum(
                item["kind"] == "logical-reference" for item in ordered_edges
            ),
        },
    }
    result["fingerprint"] = fingerprint(result)
    return result
