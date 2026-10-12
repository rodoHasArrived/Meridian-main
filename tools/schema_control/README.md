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
# Resolve the intended baseline once; retain this SHA for repeatable comparisons.
$baselineSha = git rev-parse --verify 'origin/main^{commit}'

# Fast checks that do not need PostgreSQL.
python build/scripts/schema-control.py inventory --base-ref $baselineSha

# Scaffold a migration with the next available ordinal and refresh the reservation table.
python build/scripts/schema-control.py new-migration --migration-set ledger --name example

# Regenerate the reservation table, or check that the committed table is current.
python build/scripts/schema-control.py generate-migration-docs
python build/scripts/schema-control.py generate-migration-docs --check

# Build a candidate snapshot from a disposable PostgreSQL database.
python -m pip install --requirement tools/schema_control/requirements.txt
python build/scripts/schema-control.py snapshot `
  --database-url "postgresql://meridian:meridian@localhost:5432/meridian_schema_control" `
  --base-ref $baselineSha

# Rebuild and require the candidate to match committed manifests and docs.
python build/scripts/schema-control.py verify `
  --database-url "postgresql://meridian:meridian@localhost:5432/meridian_schema_control" `
  --base-ref $baselineSha

# After reviewing a snapshot artifact, copy it to the tracked output roots.
python build/scripts/schema-control.py promote `
  --candidate-root build/schema-control/candidate
```

`snapshot` and `verify` enforce a disposable, empty database preflight before running any DDL. The
hosted workflow supplies a fresh database; do not point either command at a shared or production
database.

### Migration authoring and reservations

[Migration reservations](../../database/migration-reservations.json) is the machine-readable
register for filename conventions and pending ordinal claims in all registered migration sets.
The [blueprint reservation table](../../docs/engineering/blueprints/README.md#ledger-migration-ordinals)
is generated from that register and the SQL files on disk.

`new-migration --migration-set <id> --name <snake_case_name>` selects the next ordinal after the
highest file ordinal, skips reservations, writes a SQL scaffold, and refreshes the documentation
table. Add `--ordinal N` to request a particular free number. Occupied numbers and reserved numbers
are always refused, including the historical duplicate Ledger ordinal `008`.

Reservations are pending plans. To implement a reserved migration, remove its pending claim from
the register before scaffolding that number. Move remaining reservations when delivery sequencing
changes, preserving the planned dependency order. Preserve every applied migration's filename and
ordinal. The two historical Ledger `008` scripts remain in place; comparison against `--base-ref`
rejects newly introduced ordinal collisions while retaining that existing history.

Run `generate-migration-docs` after editing the register directly. Its `--check` mode reports a
stale generated block without updating the documentation. Run `inventory --base-ref <base-sha>`
to validate migration changes against the intended PR base.

### Baseline and candidate evidence

Pull-request runs use `github.event.pull_request.base.sha` as the migration comparison baseline.
The candidate SHA identifies the commit actually checked out by Actions; for pull requests this is
normally GitHub's merge commit. Advancing `origin/main` cannot change the comparison for the same
recorded candidate/baseline pair.

Manual `check` and `snapshot` runs require an explicit `baseline_ref` input. Supply a full commit
SHA for repeatable runs, or a Git ref that the workflow resolves once before running checks:

```powershell
gh workflow run schema-control.yml --ref <branch> -f mode=snapshot -f baseline_ref=<baseline-sha>
```

The workflow uploads `build/schema-control/revisions.json` and adds both resolved SHAs to its
step summary before testing. The CLI also writes `candidate/reports/revisions.json` and includes
both SHAs in the candidate summary, including migration-safety failures. Local CLI evidence records
whether the working tree is dirty; a local run without Git or without `--base-ref` reports the
unavailable identity as `null`. Baseline-free runs do not perform baseline migration checks.

To reproduce a comparison, check out the recorded candidate SHA, pass the recorded baseline SHA
to `--base-ref`, and use a fresh disposable database. Keep the working tree clean to reproduce the
recorded candidate. Revision evidence belongs only to run reports and is excluded from the
tracked manifests and generated documentation, so committing regenerated artifacts does not
change their contents solely because the candidate SHA changed.

## Source and output ownership

| Layer | Canonical source | Generated output |
| --- | --- | --- |
| Migration modules and schema placement | `database/schema-control.json` | `database/manifest/migrations.json` |
| Migration filenames and pending ordinal claims | `database/migration-reservations.json` plus SQL filenames | Reservation block in `docs/engineering/blueprints/README.md` |
| Physical PostgreSQL objects | SQL migrations plus a migrated PostgreSQL catalog | `database/manifest/catalog.json`, `database/manifest/schemas/*.json` |
| Public DTOs and data objects | C# source configured by `contract_sets` | `database/manifest/contracts.json` |
| Cross-object relationships | PostgreSQL dependencies, C# type references, and explicit registry edges | `database/manifest/dependencies.json` |
| Governance | `database/policies/*.json` | `database/manifest/policies.json` and candidate reports |
| Human-readable reference | The generated manifests above | `docs/generated/database/**` |

The candidate workspace is `build/schema-control/candidate/`. Only `promote` writes to the tracked
manifest and documentation roots, and it must be followed by `verify` in GitHub Actions.
Configured output roots must resolve to a non-root path within the repository; schema control
rejects absolute paths, repository escapes, and symlinks that resolve outside the checkout.

## Policy behavior

The policy engine currently checks primary keys, foreign-key index coverage, use of the `public`
schema, table comments, selected row-level security expectations, and legacy reapply migration
modules. Migration checks separately enforce registered directories, unique tracked ordinals,
new ordinal collisions against the comparison baseline, immutable applied history, reviewed
removal waivers, and destructive-change detection for new SQL.

Policy severities and narrowly reviewed exceptions belong in `database/policies/`; do not suppress
checks in the workflow or generator.

## Extending the catalog

When adding a PostgreSQL-backed module:

1. Add its SQL migration directory and real runner settings to `migration_sets`.
2. Add the relevant contract directory or namespace to `contract_sets` when one exists.
3. Declare only cross-module dependencies that PostgreSQL or C# cannot discover directly.
4. Run the inventory tests and a hosted `snapshot` dispatch.
5. Review, promote, and verify the generated manifests and diagrams in the same pull request.
