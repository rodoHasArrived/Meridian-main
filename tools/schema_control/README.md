# PostgreSQL Schema Control

Meridian schema control is a self-auditing pipeline for the PostgreSQL migrations and public C#
data contracts used by the program. It builds a disposable database, extracts PostgreSQL-specific
metadata from `pg_catalog`, evaluates governance rules, and produces deterministic manifests,
Mermaid diagrams, and review reports.

It does not update a production database from application models. SQL migrations remain the
authoritative physical schema, while C# DTOs and related public contract objects are catalogued as
a separate layer. Database-to-contract links in `database/schema-control.json` are explicit module
associations and never imply that a DTO and a table are structurally identical.

## Pipeline

```mermaid
flowchart LR
    Contracts[Public C# contracts] --> ContractCatalog[Contract manifest]
    Migrations[Versioned SQL migrations] --> Candidate[(Disposable PostgreSQL 16)]
    Candidate --> Catalog[pg_catalog manifest]
    Catalog --> Policies[Policy evaluation]
    ContractCatalog --> Dependencies[Dependency graph]
    Catalog --> Dependencies
    Policies --> Artifacts[Generated manifests, docs, and diagrams]
    Dependencies --> Artifacts
    Artifacts --> Drift[Committed-artifact drift check]
```

The registry currently covers the ten SQL migration modules under `Meridian.Storage` and
`Meridian.Identity`. Direct Lending remains a distinct migration module even though its default
physical location is the `security_master` schema.

## Commands

Run commands from the repository root:

```powershell
# Fast checks that do not need PostgreSQL.
python build/scripts/schema-control.py inventory --base-ref origin/main

# Build a candidate snapshot from a disposable PostgreSQL database.
python -m pip install --requirement tools/schema_control/requirements.txt
python build/scripts/schema-control.py snapshot `
  --database-url "postgresql://meridian:meridian@localhost:5432/meridian_schema_control"

# Rebuild and require the candidate to match committed manifests and docs.
python build/scripts/schema-control.py verify `
  --database-url "postgresql://meridian:meridian@localhost:5432/meridian_schema_control" `
  --base-ref origin/main

# After reviewing a snapshot artifact, copy it to the tracked output roots.
python build/scripts/schema-control.py promote `
  --candidate-root build/schema-control/candidate
```

`snapshot` and `verify` enforce a disposable, empty database preflight before running any DDL. The
hosted workflow supplies a fresh database; do not point either command at a shared or production
database.

## Source and output ownership

| Layer | Canonical source | Generated output |
| --- | --- | --- |
| Migration modules and schema placement | `database/schema-control.json` | `database/manifest/migrations.json` |
| Physical PostgreSQL objects | SQL migrations plus a migrated PostgreSQL catalog | `database/manifest/catalog.json`, `database/manifest/schemas/*.json` |
| Public DTOs and data objects | C# source configured by `contract_sets` | `database/manifest/contracts.json` |
| Cross-object relationships | PostgreSQL dependencies, C# type references, and explicit registry edges | `database/manifest/dependencies.json` |
| Governance | `database/policies/*.json` | `database/manifest/policies.json` and candidate reports |
| Human-readable reference | The generated manifests above | `docs/generated/database/**` |

The candidate workspace is `build/schema-control/candidate/`. Only `promote` writes to the tracked
manifest and documentation roots, and it must be followed by `verify` in GitHub Actions.

## Policy behavior

The policy engine currently checks primary keys, foreign-key index coverage, use of the `public`
schema, table comments, selected row-level security expectations, and legacy reapply migration
modules. Migration checks separately enforce registered directories, unique tracked ordinals,
immutable applied history, reviewed removal waivers, and destructive-change detection for new SQL.

Policy severities and narrowly reviewed exceptions belong in `database/policies/`; do not suppress
checks in the workflow or generator.

## Extending the catalog

When adding a PostgreSQL-backed module:

1. Add its SQL migration directory and real runner settings to `migration_sets`.
2. Add the relevant contract directory or namespace to `contract_sets` when one exists.
3. Declare only cross-module dependencies that PostgreSQL or C# cannot discover directly.
4. Run the inventory tests and a hosted `snapshot` dispatch.
5. Review, promote, and verify the generated manifests and diagrams in the same pull request.
