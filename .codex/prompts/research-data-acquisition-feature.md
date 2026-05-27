# Add Research Data Acquisition Feature

Objective: implement a research-data acquisition feature that can ingest, preview, validate, and
handoff data without overloading the workstation.

Constraints:
- Inventory existing provider, backfill, ETL, catalog, storage, lineage, and research workflow seams.
- Use paging, streaming, cancellation, and bounded preview sizes.
- Keep acquisition orchestration in services and projection state in view models.
- Record provenance, freshness, validation results, and recoverable errors.
- Add tests for cancel, retry, partial data, invalid input, and successful handoff.

Final summary must include data limits, lineage handling, tests, and cleanup/retention decisions.
