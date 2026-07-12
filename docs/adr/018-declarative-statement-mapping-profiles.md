# ADR-018: Declarative Statement Mapping Profiles and the Statement Connector Library

**Status:** accepted  
**Owner:** core-team  
**Reviewed:** 2026-07-02  
**Date:** 2026-07-02

## Problem

Reconciliation readiness (W4-RECON-001) shipped the matching engine and queue, but statements
could only enter it through a hardcoded canonical/sample-broker CSV path with compiled-in mapping
profiles. Onboarding a new custodian or broker format — different headers, delimiters, activity
codes, or a non-CSV format entirely (OFX, IB Flex XML) — required a code change and a release.
For fund-operations adoption, connector breadth is the product: operators need to import
transactional data, position data, cash balances, fees, and dividends from real custodian and
broker statements without waiting on engineering.

## Decision

Ship statement connectors **as data, not code**, in
`src/Meridian.FinancialOperations/Reconciliation/Connectors/`:

1. **Declarative, versioned mapping-profile documents** (`schemaVersion`, format `csv`/`ofx`,
   CSV options, culture and date-format hints, per-field source columns with aliases, and an
   activity-code map). Documents persist through the versioned-snapshot pattern
   (`AtomicFileWriter`, ADR-014 source-generated JSON) in
   `<dataRoot>/reconciliation/statement-mapping-profiles.json`. Built-in profiles are immutable;
   operator profiles are created, edited, and deleted at runtime through the workstation API with
   no release.
2. **A connector abstraction** (`IStatementConnector`, `IFetchingStatementConnector`) that parses
   any source into shared `StatementParseResult` records: canonical rows classified per kind
   (position, cash balance, transaction, fee, dividend), per-column mapping confidence
   (exact/alias/fuzzy/unmapped), diagnostics, and a structural fingerprint. Shipped connectors:
   profile-driven CSV (catch-all), OFX 1.x/2.x (bank + investment aggregates flattened to tag
   pseudo-columns so the same profile shape applies), IB Flex Report XML (cash-transaction types
   classified through the editable `ib-flex-v1` activity-code map), and Alpaca account activity +
   portfolio (fetch-capable through the existing `IBrokerageActivitySync`/`IBrokeragePortfolioSync`
   gateway and the existing provider credential store — no new secret storage).
3. **Normalize-to-canonical-CSV commit.** Commit renders records deterministically (fixed column
   order, invariant formatting, LF endings, delimiter-safe values) to a canonical-CSV artifact,
   retains raw + artifact as evidence, and hands the artifact to the existing
   `IStatementRunWorkflowService`. The reconciliation engine, break/case builders, and queue are
   untouched; duplicate-key idempotency is preserved because the same source bytes always produce
   the same artifact hash.
4. **Format-drift detection.** Each profile records the column set of its last accepted import;
   subsequent imports diff against it and surface added/removed columns as a warning before rows
   map incorrectly.

## Alternatives Considered

- **Extend the positional canonical-CSV importer per format:** rejected; every new custodian would
  remain a code change, and non-columnar formats do not fit the positional parser.
- **Parse directly into reconciliation aggregates per connector:** rejected; it duplicates the
  proven workflow (validation, duplicate keys, break/case construction) and makes each connector a
  reconciliation-correctness risk. Normalizing to the one format the pipeline already understands
  keeps connectors thin and testable with golden files.
- **A new secret store for scheduled fetches:** rejected; fetch-capable connectors resolve
  credentials through the existing provider credential vault and degrade to file-only import when
  no gateway is registered.

## Consequences

- New custodian CSV/OFX formats are onboarded by authoring a mapping-profile document (workstation
  editor or API), not by a release. New *structured* formats (e.g. SWIFT MT535) still require a
  connector class, but reuse the record mapper, scorer, and profile seams.
- The legacy `StatementReconciliationService` captures the profile registry at startup, so
  operator profiles created after startup are visible to the connector import path (which reads
  the catalog live) but not to the legacy reconcile-by-path surface until restart. The connector
  commit path always emits `canonical-csv-v1`, so this does not affect imports.
- Every connector needs golden-file regression fixtures
  (`tests/Meridian.Tests/TestData/Golden/statement-connectors/`) because statement formats drift;
  the deterministic-artifact test pins the idempotency contract.
- Scheduled fetching stays minimal (persisted schedules + a one-minute `BackgroundService` sweep);
  duplicate-key idempotency makes overlapping or repeated runs safe.
