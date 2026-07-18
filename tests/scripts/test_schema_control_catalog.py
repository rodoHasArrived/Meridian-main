from __future__ import annotations

import copy
import re
import unittest
from decimal import Decimal
from typing import Any

from tools.schema_control import catalog
from tools.schema_control.catalog import extract_catalog
from tools.schema_control.common import sha256_text


_QUERY_MARKER = re.compile(r"schema-control:([a-z]+)")


class _FakeCursor:
    def __init__(
        self,
        rows: dict[str, list[Any]],
        connection: "_FakeConnection",
    ) -> None:
        self._rows = rows
        self._connection = connection
        self._current = ""
        self.executions: list[tuple[str, dict[str, Any]]] = []
        self.statements: list[str] = []
        self.transaction_states: list[bool] = []
        self.closed = False

    def execute(
        self,
        query: str,
        params: dict[str, Any] | None = None,
    ) -> None:
        marker = _QUERY_MARKER.search(query)
        if marker is None:
            raise AssertionError(f"Catalog query has no test marker: {query[:80]}")
        self._current = marker.group(1)
        self.executions.append((self._current, copy.deepcopy(params or {})))
        self.statements.append(query)
        self.transaction_states.append(self._connection.in_transaction)

    def fetchall(self) -> list[Any]:
        return list(self._rows.get(self._current, []))

    def close(self) -> None:
        self.closed = True


class _FakeConnection:
    def __init__(self, rows: dict[str, list[Any]]) -> None:
        self.in_transaction = False
        self.transactions_started = 0
        self.transactions_completed = 0
        self.transactions_rolled_back = 0
        self.fake_cursor = _FakeCursor(rows, self)
        self.closed = False

    def cursor(self) -> _FakeCursor:
        return self.fake_cursor

    def transaction(self) -> "_FakeTransaction":
        return _FakeTransaction(self)

    def close(self) -> None:
        self.closed = True


class _FakeTransaction:
    def __init__(self, connection: _FakeConnection) -> None:
        self._connection = connection

    def __enter__(self) -> None:
        if self._connection.in_transaction:
            raise AssertionError("Nested fake catalog transaction.")
        self._connection.in_transaction = True
        self._connection.transactions_started += 1

    def __exit__(self, exc_type: Any, exc: Any, traceback: Any) -> None:
        self._connection.in_transaction = False
        if exc_type is None:
            self._connection.transactions_completed += 1
        else:
            self._connection.transactions_rolled_back += 1


def _config() -> dict[str, Any]:
    return {
        "manifest": {"format": "test.schema-control.v1"},
        "migration_sets": [
            {
                "id": "security-master",
                "display_name": "Security Master",
                "schema": "security_master",
                "owner": "deployment_role_must_not_leak",
            },
            {
                "id": "accounting",
                "display_name": "Accounting",
                "schema": "ledger",
            },
            {
                "id": "direct-lending",
                "display_name": "Direct Lending",
                "schema": "security_master",
            },
        ],
    }


def _catalog_rows() -> dict[str, list[dict[str, Any]]]:
    return {
        "schemas": [
            {
                "schema_name": "security_master",
                "owner": "meridian",
                "acl": [],
                "security_labels": [],
                "comment": "Security master.",
            },
            {
                "schema_name": "ledger",
                "owner": "meridian",
                "acl": [
                    {
                        "grantee": "meridian_readonly",
                        "grantor": "meridian",
                        "privilege": "USAGE",
                        "grantable": False,
                    },
                    {
                        "grantee": "PUBLIC",
                        "grantor": "meridian",
                        "privilege": "usage",
                        "grantable": False,
                    },
                ],
                "security_labels": [
                    {"provider": "selinux", "label": "system_u:object_r:ledger_t:s0"}
                ],
                "comment": "Ledger\r\nobjects.",
            },
        ],
        "relations": [
            {
                "schema_name": "security_master",
                "relation_name": "instrument",
                "relation_kind": "table",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "comment": "Tradable instrument.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "upstream_events",
                "relation_kind": "foreign_table",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "foreign_server": "integration_server",
                "foreign_options": ["schema_name=upstream", "table_name=events"],
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_sequence",
                "relation_kind": "sequence",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "sequence_data_type": "bigint",
                "sequence_start": 1000,
                "sequence_increment": 10,
                "sequence_minimum": 1,
                "sequence_maximum": 9_223_372_036_854_775_807,
                "sequence_cache": 25,
                "sequence_cycle": False,
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "materialized_balance",
                "relation_kind": "materialized_view",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_view",
                "relation_kind": "view",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "owner": "meridian",
                "acl": [],
                "security_labels": [],
                "relation_options": ["security_invoker=true", "security_barrier=true"],
                "replica_identity": "default",
                "definition": " SELECT entry_id, amount\r\nFROM ledger.journal_entry ",
                "comment": "Journal reporting view.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "posting_batch",
                "relation_kind": "partitioned_table",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "comment": "Posting batches.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "relation_kind": "table",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": True,
                "rls_forced": True,
                "owner": "meridian",
                "acl": [
                    {
                        "grantee": "meridian_application",
                        "grantor": "meridian",
                        "privilege": "SELECT",
                        "grantable": False,
                    },
                    {
                        "grantee": "meridian",
                        "grantor": "meridian",
                        "privilege": "insert",
                        "grantable": True,
                    },
                ],
                "security_labels": [
                    {"provider": "selinux", "label": "system_u:object_r:journal_t:s0"}
                ],
                "relation_options": ["fillfactor=80"],
                "replica_identity": "index",
                "comment": "Posted accounting journal.",
            },
        ],
        "columns": [
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "ordinal_position": Decimal("2"),
                "column_name": "amount",
                "data_type": "numeric(20,4)",
                "type_schema": "pg_catalog",
                "type_name": "numeric",
                "nullable": False,
                "default_expression": " 0.0000\r\n",
                "identity_generation": None,
                "generated_kind": None,
                "generated_expression": None,
                "collation": None,
                "security_labels": [
                    {
                        "provider": "selinux",
                        "label": "system_u:object_r:amount_t:s0",
                    }
                ],
                "comment": "Amount in the entry currency.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "ordinal_position": 1,
                "column_name": "entry_id",
                "data_type": "bigint",
                "type_schema": "pg_catalog",
                "type_name": "int8",
                "nullable": False,
                "default_expression": None,
                "identity_generation": "always",
                "generated_kind": None,
                "generated_expression": None,
                "collation": None,
                "comment": "Stable journal identifier.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "ordinal_position": 3,
                "column_name": "amount_abs",
                "data_type": "numeric(20,4)",
                "type_schema": "pg_catalog",
                "type_name": "numeric",
                "nullable": True,
                "default_expression": None,
                "identity_generation": None,
                "generated_kind": "stored",
                "generated_expression": " abs(amount) \r\n",
                "collation": None,
                "comment": None,
            },
            {
                "schema_name": "security_master",
                "relation_name": "instrument",
                "ordinal_position": 1,
                "column_name": "instrument_id",
                "data_type": "uuid",
                "type_schema": "pg_catalog",
                "type_name": "uuid",
                "nullable": False,
                "default_expression": None,
                "identity_generation": None,
                "generated_kind": None,
                "generated_expression": None,
                "collation": None,
                "comment": None,
            },
        ],
        "constraints": [
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "constraint_name": "uq_journal_amount",
                "constraint_type": "unique",
                "source_columns": ["amount"],
                "referenced_schema": None,
                "referenced_table": None,
                "referenced_columns": [],
                "definition": " UNIQUE (amount) ",
                "deferrable": False,
                "initially_deferred": False,
                "validated": True,
                "no_inherit": False,
                "match_type": None,
                "update_action": None,
                "delete_action": None,
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "constraint_name": "pk_journal_entry",
                "constraint_type": "primary_key",
                "source_columns": ["entry_id"],
                "referenced_schema": None,
                "referenced_table": None,
                "referenced_columns": [],
                "definition": "PRIMARY KEY (entry_id)",
                "deferrable": False,
                "initially_deferred": False,
                "validated": True,
                "no_inherit": False,
                "match_type": None,
                "update_action": None,
                "delete_action": None,
                "comment": "Primary journal key.",
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "constraint_name": "fk_journal_instrument",
                "constraint_type": "foreign_key",
                "source_columns": ["instrument_id", "instrument_scope"],
                "referenced_schema": "security_master",
                "referenced_table": "instrument",
                "referenced_columns": ["instrument_id", "scope"],
                "definition": "FOREIGN KEY (instrument_id, instrument_scope)\r\n  REFERENCES security_master.instrument(instrument_id, scope)",
                "deferrable": True,
                "initially_deferred": True,
                "validated": True,
                "no_inherit": False,
                "match_type": "full",
                "update_action": "no_action",
                "delete_action": "restrict",
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "constraint_name": "ck_journal_amount",
                "constraint_type": "check",
                "source_columns": ["amount"],
                "referenced_schema": None,
                "referenced_table": None,
                "referenced_columns": [],
                "definition": "CHECK (amount <> 0)",
                "deferrable": False,
                "initially_deferred": False,
                "validated": True,
                "no_inherit": True,
                "match_type": None,
                "update_action": None,
                "delete_action": None,
                "comment": None,
            },
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "constraint_name": "ex_journal_period",
                "constraint_type": "exclusion",
                "source_columns": ["entry_id"],
                "referenced_schema": None,
                "referenced_table": None,
                "referenced_columns": [],
                "definition": "EXCLUDE USING gist (entry_id WITH =)",
                "deferrable": False,
                "initially_deferred": False,
                "validated": True,
                "no_inherit": False,
                "match_type": None,
                "update_action": None,
                "delete_action": None,
                "comment": None,
            },
        ],
        "indexes": [
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "index_name": "ix_journal_amount",
                "method": "btree",
                "is_unique": False,
                "is_primary": False,
                "valid": True,
                "ready": True,
                "live": True,
                "is_replica_identity": True,
                "owner": "meridian",
                "index_options": ["fillfactor=70"],
                "security_labels": [
                    {
                        "provider": "selinux",
                        "label": "system_u:object_r:index_t:s0",
                    }
                ],
                "key_columns": ["lower((amount)::text)"],
                "included_columns": ["entry_id"],
                "predicate": " amount <> 0 \r\n",
                "expression": " lower((amount)::text) ",
                "definition": " CREATE INDEX ix_journal_amount\r\nON ledger.journal_entry USING btree (lower((amount)::text)) ",
                "comment": "Lookup index.",
            }
        ],
        "routines": [
            {
                "schema_name": "ledger",
                "routine_name": "post_transaction",
                "signature": "post_transaction(jsonb)",
                "routine_kind": "function",
                "result_type": "bigint",
                "language": "plpgsql",
                "volatility": "volatile",
                "parallel_safety": "unsafe",
                "security_definer": True,
                "strict": True,
                "owner": "meridian",
                "acl": [
                    {
                        "grantee": "meridian_application",
                        "grantor": "meridian",
                        "privilege": "EXECUTE",
                        "grantable": False,
                    }
                ],
                "security_labels": [
                    {
                        "provider": "selinux",
                        "label": "system_u:object_r:posting_t:s0",
                    }
                ],
                "definition": " CREATE FUNCTION ledger.post_transaction(payload jsonb)\r\nRETURNS bigint ... \r\n",
                "comment": "Controlled posting entry point.",
            },
            {
                "schema_name": "ledger",
                "routine_name": "close_period",
                "signature": "close_period(date)",
                "routine_kind": "procedure",
                "result_type": None,
                "language": "plpgsql",
                "volatility": "volatile",
                "parallel_safety": "unsafe",
                "security_definer": False,
                "strict": False,
                "definition": "CREATE PROCEDURE ledger.close_period(day date) ...",
                "comment": None,
            },
        ],
        "triggers": [
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "trigger_name": "journal_immutable",
                "timing": "before",
                "events": ["update", "delete"],
                "level": "row",
                "enabled": "origin",
                "function_schema": "ledger",
                "function_signature": "reject_posted_change()",
                "definition": " CREATE TRIGGER journal_immutable\r\nBEFORE UPDATE OR DELETE ON ledger.journal_entry ... ",
                "comment": "Posted entries are immutable.",
            }
        ],
        "enums": [
            {
                "schema_name": "ledger",
                "type_name": "entry_side",
                "owner": "meridian",
                "labels": ["debit", "credit"],
                "security_labels": [],
                "comment": "Accounting side.",
            }
        ],
        "domains": [
            {
                "schema_name": "ledger",
                "type_name": "positive_amount",
                "owner": "meridian",
                "base_type": "numeric(20,4)",
                "not_null": True,
                "default_expression": " 1.0000 ",
                "constraints": ["CHECK (VALUE > 0)"],
                "collation": None,
                "security_labels": [],
                "comment": "Strictly positive amount.",
            }
        ],
        "composites": [
            {
                "schema_name": "ledger",
                "type_name": "money_value",
                "owner": "meridian",
                "attribute_name": "currency",
                "ordinal_position": 2,
                "data_type": "text",
                "nullable": True,
                "security_labels": [],
                "comment": "Money value.",
                "attribute_comment": "ISO currency.",
            },
            {
                "schema_name": "ledger",
                "type_name": "money_value",
                "owner": "meridian",
                "attribute_name": "amount",
                "ordinal_position": 1,
                "data_type": "numeric(20,4)",
                "nullable": True,
                "security_labels": [],
                "comment": "Money value.",
                "attribute_comment": None,
            },
        ],
        "policies": [
            {
                "schema_name": "ledger",
                "relation_name": "journal_entry",
                "policy_name": "journal_book_access",
                "permissive": True,
                "command": "select",
                "using_expression": " current_setting('app.book_id') = book_id::text ",
                "check_expression": None,
                "target_roles": ["meridian_readonly", "PUBLIC"],
            }
        ],
        "partitions": [
            {
                "schema_name": "ledger",
                "partition_name": "posting_batch_2026",
                "parent_schema": "ledger",
                "parent_name": "posting_batch",
                "strategy": "range",
                "key_definition": "RANGE (posting_date)",
                "bound": " FOR VALUES FROM ('2026-01-01') TO ('2027-01-01') ",
            }
        ],
        "extensions": [
            {
                "schema_name": "ledger",
                "extension_name": "btree_gist",
                "owner": "meridian",
                "version": "1.7",
                "relocatable": True,
                "security_labels": [],
                "comment": "B-tree operator classes for GiST.",
            }
        ],
        "dependencies": [
            {
                "source_schema": "ledger",
                "source_name": "journal_view",
                "source_kind": "view",
                "source_identity": "journal_view",
                "target_schema": "ledger",
                "target_name": "journal_entry",
                "target_kind": "table",
                "target_identity": "journal_entry",
                "dependency_kind": "normal",
            },
            {
                "source_schema": "ledger",
                "source_name": "journal_view",
                "source_kind": "view",
                "source_identity": "journal_view",
                "target_schema": "ledger",
                "target_name": "journal_entry",
                "target_kind": "table",
                "target_identity": "journal_entry",
                "dependency_kind": "normal",
            },
        ],
    }


def _extract(
    rows: dict[str, list[Any]],
    config: dict[str, Any] | None = None,
) -> tuple[dict[str, Any], _FakeConnection, list[str]]:
    urls: list[str] = []
    connection = _FakeConnection(rows)

    def factory(database_url: str) -> _FakeConnection:
        urls.append(database_url)
        return connection

    manifest = extract_catalog(
        "postgresql://catalog-test",
        config or _config(),
        connection_factory=factory,
    )
    return manifest, connection, urls


class CatalogExtractionTests(unittest.TestCase):
    def test_catalog_queries_do_not_use_collation_as_a_relation_alias(self) -> None:
        query_text = "\n".join(query for _, query in catalog._QUERIES)

        self.assertNotIn("pg_collation collation on", query_text)
        self.assertIn("pg_collation collation_record on", query_text)

    def test_extracts_postgresql_categories_and_logical_schema_mapping(self) -> None:
        manifest, connection, urls = _extract(_catalog_rows())

        self.assertEqual(["postgresql://catalog-test"], urls)
        self.assertTrue(connection.closed)
        self.assertTrue(connection.fake_cursor.closed)
        self.assertEqual(17, len(connection.fake_cursor.executions))
        self.assertEqual(
            ["snapshot", "searchpath", "timezone"],
            [marker for marker, _ in connection.fake_cursor.executions[:3]],
        )
        self.assertEqual(1, connection.transactions_started)
        self.assertEqual(1, connection.transactions_completed)
        self.assertEqual(0, connection.transactions_rolled_back)
        self.assertTrue(all(connection.fake_cursor.transaction_states))
        self.assertEqual(
            ["ledger", "security_master"],
            connection.fake_cursor.executions[3][1]["schemas"],
        )
        setup_sql = "\n".join(connection.fake_cursor.statements[:3]).lower()
        self.assertIn("repeatable read", setup_sql)
        self.assertIn("read only", setup_sql)
        self.assertIn("search_path = pg_catalog", setup_sql)
        self.assertIn("time zone 'utc'", setup_sql)
        catalog_sql = "\n".join(connection.fake_cursor.statements[3:]).lower()
        self.assertIn("pg_catalog.aclexplode", catalog_sql)
        self.assertIn("pg_catalog.pg_seclabel", catalog_sql)
        self.assertIn("policy.polroles", catalog_sql)
        self.assertIn("relation.reloptions", catalog_sql)
        self.assertIn("index_record.indisreplident", catalog_sql)
        self.assertEqual("test.schema-control.v1", manifest["format"])
        self.assertEqual(["ledger", "security_master"], manifest["physical_schemas"])
        self.assertEqual(
            ["accounting", "direct-lending", "security-master"],
            [item["id"] for item in manifest["module_schema_mapping"]],
        )
        self.assertNotIn("owner", manifest["module_schema_mapping"][0])

        ledger = manifest["schemas"][0]
        self.assertEqual("ledger", ledger["name"])
        self.assertEqual(["accounting"], ledger["logical_modules"])
        self.assertEqual("meridian", ledger["owner"])
        self.assertEqual(
            ["PUBLIC", "meridian_readonly"],
            [entry["grantee"] for entry in ledger["acl"]],
        )
        self.assertEqual("usage", ledger["acl"][0]["privilege"])
        self.assertEqual("selinux", ledger["security_labels"][0]["provider"])
        self.assertEqual(
            ["journal_entry", "posting_batch"],
            [table["name"] for table in ledger["tables"]],
        )
        self.assertEqual(["journal_view"], [view["name"] for view in ledger["views"]])
        self.assertEqual(
            ["materialized_balance"],
            [view["name"] for view in ledger["materialized_views"]],
        )
        self.assertEqual(
            ["journal_sequence"], [item["name"] for item in ledger["sequences"]]
        )
        self.assertEqual(
            ["upstream_events"], [item["name"] for item in ledger["foreign_tables"]]
        )
        self.assertEqual(
            {
                "data_type": "bigint",
                "start": 1000,
                "increment": 10,
                "minimum": 1,
                "maximum": 9_223_372_036_854_775_807,
                "cache": 25,
                "cycle": False,
            },
            ledger["sequences"][0]["sequence"],
        )
        self.assertEqual(
            "integration_server", ledger["foreign_tables"][0]["foreign_server"]
        )
        self.assertEqual(
            ["schema_name=upstream", "table_name=events"],
            ledger["foreign_tables"][0]["foreign_options"],
        )
        self.assertEqual(
            "SELECT entry_id, amount\nFROM ledger.journal_entry",
            ledger["views"][0]["definition"],
        )
        self.assertEqual(
            ["security_barrier=true", "security_invoker=true"],
            ledger["views"][0]["options"],
        )
        self.assertRegex(ledger["views"][0]["definition_hash"], r"^[0-9a-f]{64}$")

        journal = ledger["tables"][0]
        self.assertEqual("meridian", journal["owner"])
        self.assertEqual("index", journal["replica_identity"])
        self.assertEqual(["fillfactor=80"], journal["options"])
        self.assertEqual(
            ["meridian", "meridian_application"],
            [entry["grantee"] for entry in journal["acl"]],
        )
        self.assertEqual("insert", journal["acl"][0]["privilege"])
        self.assertEqual("selinux", journal["security_labels"][0]["provider"])
        self.assertEqual(
            ["entry_id", "amount", "amount_abs"],
            [column["name"] for column in journal["columns"]],
        )
        self.assertEqual("0.0000", journal["columns"][1]["default"])
        self.assertEqual(
            "selinux", journal["columns"][1]["security_labels"][0]["provider"]
        )
        self.assertEqual("always", journal["columns"][0]["identity"])
        self.assertEqual("stored", journal["columns"][2]["generated"])
        self.assertEqual("abs(amount)", journal["columns"][2]["generated_expression"])
        self.assertEqual(
            {"check", "exclusion", "foreign_key", "primary_key", "unique"},
            {constraint["type"] for constraint in journal["constraints"]},
        )
        foreign_key = next(
            constraint
            for constraint in journal["constraints"]
            if constraint["type"] == "foreign_key"
        )
        self.assertEqual(["instrument_id", "instrument_scope"], foreign_key["columns"])
        self.assertEqual("security_master", foreign_key["referenced_schema"])
        self.assertEqual(["instrument_id", "scope"], foreign_key["referenced_columns"])
        self.assertNotIn("\r", foreign_key["definition"])

        index = journal["indexes"][0]
        self.assertEqual("btree", index["method"])
        self.assertEqual(["lower((amount)::text)"], index["key_columns"])
        self.assertEqual(["entry_id"], index["included_columns"])
        self.assertEqual("amount <> 0", index["predicate"])
        self.assertEqual("lower((amount)::text)", index["expression"])
        self.assertTrue(index["is_replica_identity"])
        self.assertEqual("meridian", index["owner"])
        self.assertEqual(["fillfactor=70"], index["options"])
        self.assertEqual("selinux", index["security_labels"][0]["provider"])
        self.assertEqual("journal_immutable", journal["triggers"][0]["name"])
        self.assertEqual("journal_book_access", journal["policies"][0]["name"])
        self.assertEqual(
            ["PUBLIC", "meridian_readonly"],
            journal["policies"][0]["target_roles"],
        )

        routine = ledger["functions"][0]
        self.assertEqual("post_transaction(jsonb)", routine["signature"])
        self.assertEqual("bigint", routine["result_type"])
        self.assertTrue(routine["security_definer"])
        self.assertEqual("meridian", routine["owner"])
        self.assertEqual("execute", routine["acl"][0]["privilege"])
        self.assertEqual("selinux", routine["security_labels"][0]["provider"])
        expected_definition = (
            "CREATE FUNCTION ledger.post_transaction(payload jsonb)\n"
            "RETURNS bigint ..."
        )
        self.assertEqual(sha256_text(expected_definition), routine["definition_hash"])
        self.assertEqual(
            ["close_period(date)"], [item["signature"] for item in ledger["procedures"]]
        )
        self.assertEqual(["entry_side"], [item["name"] for item in ledger["enums"]])
        self.assertEqual("meridian", ledger["enums"][0]["owner"])
        self.assertEqual(["debit", "credit"], ledger["enums"][0]["labels"])
        self.assertEqual(
            ["positive_amount"], [item["name"] for item in ledger["domains"]]
        )
        self.assertEqual("1.0000", ledger["domains"][0]["default_expression"])
        self.assertEqual(
            ["money_value"], [item["name"] for item in ledger["composite_types"]]
        )
        self.assertEqual(
            ["amount", "currency"],
            [item["name"] for item in ledger["composite_types"][0]["attributes"]],
        )
        self.assertEqual("posting_batch_2026", ledger["partitions"][0]["name"])
        self.assertEqual("btree_gist", ledger["extensions"][0]["name"])
        self.assertEqual("meridian", ledger["extensions"][0]["owner"])
        self.assertEqual("ledger.journal_view", ledger["dependencies"][0]["source"])
        self.assertEqual("ledger.journal_entry", ledger["dependencies"][0]["target"])
        self.assertEqual(1, len(manifest["dependencies"]))
        rls = next(
            item
            for item in ledger["row_level_security"]
            if item["relation"] == "journal_entry"
        )
        self.assertTrue(rls["enabled"])
        self.assertTrue(rls["forced"])
        self.assertEqual(["journal_book_access"], rls["policies"])
        self.assertEqual("Ledger\nobjects.", ledger["comment"])
        self.assertTrue(
            any(item["object_type"] == "column" for item in ledger["comments"])
        )

        security_master = manifest["schemas"][1]
        self.assertEqual(
            ["direct-lending", "security-master"],
            security_master["logical_modules"],
        )

    def test_output_and_fingerprints_are_independent_of_row_and_config_order(
        self,
    ) -> None:
        rows = _catalog_rows()
        first, _, _ = _extract(rows)
        reversed_rows = {name: list(reversed(values)) for name, values in rows.items()}
        reversed_config = _config()
        reversed_config["migration_sets"].reverse()

        second, _, _ = _extract(reversed_rows, reversed_config)

        self.assertEqual(first, second)
        self.assertRegex(first["database_fingerprint"], r"^[0-9a-f]{64}$")
        self.assertEqual(
            sorted(first["object_fingerprints"]),
            list(first["object_fingerprints"]),
        )
        self.assertGreater(len(first["object_fingerprints"]), 20)
        for value in first["object_fingerprints"].values():
            self.assertRegex(value, r"^[0-9a-f]{64}$")
        for schema in first["schemas"]:
            self.assertEqual(
                schema["fingerprint"],
                first["object_fingerprints"][f"schema:{schema['name']}"],
            )
        self.assertIn(
            "table:ledger.journal_entry",
            first["object_fingerprints"],
        )
        self.assertIn(
            "function:ledger.post_transaction(jsonb)",
            first["object_fingerprints"],
        )

    def test_audit_schemas_are_inspected_without_becoming_physical_modules(
        self,
    ) -> None:
        config = _config()
        config["audit_schemas"] = ["public", "ledger", "public"]
        rows = _catalog_rows()
        rows["schemas"].append({"schema_name": "public", "comment": None})
        rows["relations"].append(
            {
                "schema_name": "public",
                "relation_name": "unexpected_application_table",
                "relation_kind": "table",
                "persistence": "permanent",
                "is_partition": False,
                "rls_enabled": False,
                "rls_forced": False,
                "comment": "Policy should report this object.",
            }
        )

        manifest, connection, _ = _extract(rows, config)

        self.assertEqual(["ledger", "security_master"], manifest["physical_schemas"])
        self.assertEqual(
            ["ledger", "public", "security_master"],
            manifest["inspected_schemas"],
        )
        for _, params in connection.fake_cursor.executions[3:]:
            self.assertEqual(manifest["inspected_schemas"], params["schemas"])
        public = next(
            schema for schema in manifest["schemas"] if schema["name"] == "public"
        )
        self.assertEqual([], public["logical_modules"])
        self.assertEqual(
            ["unexpected_application_table"],
            [table["name"] for table in public["tables"]],
        )
        self.assertNotIn(
            "public",
            {item["physical_schema"] for item in manifest["module_schema_mapping"]},
        )

    def test_structural_change_updates_object_schema_and_database_fingerprints(
        self,
    ) -> None:
        original, _, _ = _extract(_catalog_rows())
        changed_rows = _catalog_rows()
        journal = next(
            row
            for row in changed_rows["relations"]
            if row["schema_name"] == "ledger"
            and row["relation_name"] == "journal_entry"
        )
        journal["comment"] = "Updated journal contract."

        changed, _, _ = _extract(changed_rows)

        self.assertNotEqual(
            original["object_fingerprints"]["table:ledger.journal_entry"],
            changed["object_fingerprints"]["table:ledger.journal_entry"],
        )
        self.assertNotEqual(
            original["object_fingerprints"]["schema:ledger"],
            changed["object_fingerprints"]["schema:ledger"],
        )
        self.assertNotEqual(
            original["database_fingerprint"], changed["database_fingerprint"]
        )

    def test_security_metadata_is_ordered_and_changes_fingerprints(self) -> None:
        original_rows = _catalog_rows()
        original, _, _ = _extract(original_rows)

        reordered_rows = _catalog_rows()
        ledger_schema = next(
            row for row in reordered_rows["schemas"] if row["schema_name"] == "ledger"
        )
        ledger_schema["acl"].reverse()
        journal = next(
            row
            for row in reordered_rows["relations"]
            if row["schema_name"] == "ledger"
            and row["relation_name"] == "journal_entry"
        )
        journal["acl"].reverse()
        journal["relation_options"].reverse()
        policy = reordered_rows["policies"][0]
        policy["target_roles"].reverse()

        reordered, _, _ = _extract(reordered_rows)
        self.assertEqual(original, reordered)

        changed_rows = _catalog_rows()
        changed_journal = next(
            row
            for row in changed_rows["relations"]
            if row["schema_name"] == "ledger"
            and row["relation_name"] == "journal_entry"
        )
        changed_journal["acl"][0]["privilege"] = "UPDATE"
        changed, _, _ = _extract(changed_rows)

        key = "table:ledger.journal_entry"
        self.assertNotEqual(
            original["object_fingerprints"][key],
            changed["object_fingerprints"][key],
        )
        self.assertNotEqual(
            original["object_fingerprints"]["schema:ledger"],
            changed["object_fingerprints"]["schema:ledger"],
        )
        self.assertNotEqual(
            original["database_fingerprint"], changed["database_fingerprint"]
        )

    def test_each_security_contract_participates_in_object_fingerprints(self) -> None:
        original, _, _ = _extract(_catalog_rows())

        def schema(rows: dict[str, list[dict[str, Any]]]) -> dict[str, Any]:
            return next(
                row for row in rows["schemas"] if row["schema_name"] == "ledger"
            )

        def relation(rows: dict[str, list[dict[str, Any]]]) -> dict[str, Any]:
            return next(
                row
                for row in rows["relations"]
                if row["schema_name"] == "ledger"
                and row["relation_name"] == "journal_entry"
            )

        cases = [
            (
                "schema owner",
                "schema:ledger",
                lambda rows: schema(rows).__setitem__("owner", "schema_owner_v2"),
            ),
            (
                "schema security label",
                "schema:ledger",
                lambda rows: schema(rows)["security_labels"][0].__setitem__(
                    "label", "system_u:object_r:ledger_v2_t:s0"
                ),
            ),
            (
                "relation options",
                "table:ledger.journal_entry",
                lambda rows: relation(rows).__setitem__(
                    "relation_options", ["fillfactor=60"]
                ),
            ),
            (
                "relation replica identity",
                "table:ledger.journal_entry",
                lambda rows: relation(rows).__setitem__("replica_identity", "full"),
            ),
            (
                "replica identity index",
                "index:ledger.ix_journal_amount",
                lambda rows: rows["indexes"][0].__setitem__(
                    "is_replica_identity", False
                ),
            ),
            (
                "column security label",
                "column:ledger.journal_entry.amount",
                lambda rows: rows["columns"][0]["security_labels"][0].__setitem__(
                    "label", "system_u:object_r:amount_v2_t:s0"
                ),
            ),
            (
                "routine ACL",
                "function:ledger.post_transaction(jsonb)",
                lambda rows: rows["routines"][0]["acl"][0].__setitem__(
                    "grantable", True
                ),
            ),
            (
                "policy target roles",
                "policy:ledger.journal_entry.journal_book_access",
                lambda rows: rows["policies"][0].__setitem__(
                    "target_roles", ["meridian_auditor"]
                ),
            ),
        ]

        for label, key, mutate in cases:
            with self.subTest(label=label):
                changed_rows = _catalog_rows()
                mutate(changed_rows)
                changed, _, _ = _extract(changed_rows)
                self.assertNotEqual(
                    original["object_fingerprints"][key],
                    changed["object_fingerprints"][key],
                )
                self.assertNotEqual(
                    original["database_fingerprint"],
                    changed["database_fingerprint"],
                )

    def test_invalid_config_fails_before_opening_connection(self) -> None:
        opened = False

        def factory(_: str) -> _FakeConnection:
            nonlocal opened
            opened = True
            return _FakeConnection({})

        with self.assertRaisesRegex(ValueError, "migration_sets"):
            extract_catalog(
                "postgresql://catalog-test",
                {},
                connection_factory=factory,
            )

        self.assertFalse(opened)

    def test_non_mapping_cursor_rows_fail_and_resources_are_closed(self) -> None:
        rows: dict[str, list[Any]] = {"schemas": [("ledger", None)]}
        connection = _FakeConnection(rows)

        with self.assertRaisesRegex(TypeError, "mapping rows"):
            extract_catalog(
                "postgresql://catalog-test",
                _config(),
                connection_factory=lambda _: connection,
            )

        self.assertTrue(connection.fake_cursor.closed)
        self.assertTrue(connection.closed)
        self.assertEqual(1, connection.transactions_rolled_back)


if __name__ == "__main__":
    unittest.main()
