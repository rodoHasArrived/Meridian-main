# Design Module Conformance

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-06-05

This document explains how Meridian physically conforms to the recommended module structure in the
[Meridian Design Document](../product/meridian-design-document.md). The design document names
bounded-context modules. Each design module now has a real source project under `src/`, a
bounded-context descriptor, and the required implementation facets. The broader design-doc
adaptation contract lives in [Design Document Adaptation](design-document-adaptation.md).

The machine-readable map is [`design-module-conformance.yml`](design-module-conformance.yml).
Validate it with:

```bash
python build/scripts/docs/validate-design-module-conformance.py --summary
```

## Current Conformance Model

| Design module | Physical project | Current implementation ownership |
| --- | --- | --- |
| `Meridian.Platform` | `src/Meridian.Platform` | `src/Meridian`, `src/Meridian.Core`, `src/Meridian.Application`, `src/Meridian.Storage` |
| `Meridian.Identity` | `src/Meridian.Identity` | scoped-access services, stores, auth role/permission/scoped-access contracts, login sessions, user profiles, auth-mode resolution, and role-profile persistence now live in `src/Meridian.Identity`; browser/WPF identity presentation still bridges through UI owners |
| `Meridian.Entities` | `src/Meridian.Entities` | fund-structure ledger-group assignment type policy, assignment-reference normalization, account ledger-group resolution, ownership-link compatibility/cycle/primary/percent/replacement-window policy, single-operating-parent policy, and governance cash-flow query policy now live in `src/Meridian.Entities`; remaining legal entity, fund, account, assignment, hierarchy, persistence policy, storage-backed entity read/write, and UI read-model behavior still bridges through application, contracts, storage, and UI shared |
| `Meridian.DataIntegration` | `src/Meridian.DataIntegration` | QuickBooks accounting-system fixture, online adapter, token exchange, connection verification, credential-backed connection store, read-only company evidence import, DTO mapping, provider credential catalog, encrypted provider credential store, credential status records, OAuth token records, verification metadata, and provider-environment normalization now live in `src/Meridian.DataIntegration`; remaining provider SDK, infrastructure adapters, storage, application ingestion, UI shared, and dashboard data routes still bridge through current owners |
| `Meridian.ReferenceData` | `src/Meridian.ReferenceData` | Security Master asset-class mapping, instrument-kind mapping, the profile catalog contract, and seeded approved custom/private asset profile templates now live in `src/Meridian.ReferenceData`; remaining identifier validation, classification/taxonomy governance, and Security Master reference-data read models still bridge through application, contracts, storage, F# aggregates, and UI shared; asset-specific contract/reference projections belong in `Meridian.Instruments` |
| `Meridian.Instruments` | `src/Meridian.Instruments` | storage-backed bond, option, equity, futures, FX spot, crypto, deposit, certificate-of-deposit, commodity, swap, and money-market fund contract/reference projection services now live in `src/Meridian.Instruments`; remaining instrument terms, obligations, lifecycle schedules, ledger/F# kernels, and storage projections still bridge through application, contracts, ledger/F# kernels, and storage |
| `Meridian.PortfolioRecords` | `src/Meridian.PortfolioRecords` | portfolio/fund records, positions, activity, holdings, ledger, shared UI services, browser, and WPF |
| `Meridian.FinancialOperations` | `src/Meridian.FinancialOperations` | Operations Continuity aggregate, command workflow service, status derivation, repositories, audit hashing, Postgres workflow store, approval-policy matrix, close-calendar services, accounting-system GL import/reconciliation service, statement reconciliation services, statement-run workflow, validation, matching engines, break classification, decision journals, and reconciliation repositories now live in `src/Meridian.FinancialOperations`; remaining accounting records, approval/casework support, close/reconciliation adapters, ledger, browser, and WPF workflows still bridge through application/UI owners |
| `Meridian.Workflow` | `src/Meridian.Workflow` | application workflows, shared workflow library, operator actions, continuity flows, browser, and WPF |
| `Meridian.Audit` | `src/Meridian.Audit` | evidence packets, audit hashes, retained manifests, lineage, archival, and export verification |
| `Meridian.Reporting` | `src/Meridian.Reporting` | reporting contracts, ledger report packs, shared reporting services, UI services, browser, and WPF |
| `Meridian.Documents` | `src/Meridian.Documents` | retained document attachments and evidence manifests inside contracts, storage, and shared UI services |

## Migration Rules

1. Do not move implementation code into a physical design-module project until the bounded context
   has stable contracts, tests, and dependency direction.
2. For new work, name the design module in the blueprint, issue, or PR description, then use its
   physical project plus the current source owners from `design-module-conformance.yml`.
3. Shared contracts remain in `src/Meridian.Contracts` until the owning bounded context can publish
   stable APIs without breaking browser, WPF, host, and test consumers. Identity auth contracts now
   publish from `src/Meridian.Identity`.
4. UI work remains in `src/Meridian.Ui/dashboard`, `src/Meridian.Wpf`, `src/Meridian.Ui.Shared`,
   and `src/Meridian.Ui.Services`; product state belongs in shared services and view models, not
   duplicated in UI leaf components.
5. Physical extraction requires a characterization test lane, dependency graph review, source README
   update, and narrow build/test proof for every moved project.

## Near-Term Implementation Extraction Candidates

The safest first implementation extraction candidates are the modules whose current behavior is
already cohesive but still spread across layer-oriented projects:

| Candidate | Reason | Required proof before extraction |
| --- | --- | --- |
| `Meridian.Identity` | Scoped-access services, stores, auth contracts, login/session state, user profile loading, auth-mode resolution, and role-profile persistence have moved; endpoint/middleware adapters remain in UI Shared until presentation contracts are characterized. | Current auth/scoped-access behavior characterized; browser and WPF session flows pass focused tests. |
| `Meridian.Entities` | Ledger-group assignment normalization, account ledger-group resolution, ownership graph policy, and governance cash-flow query policy have moved; fund structure, legal entity, account, and assignment ownership still form the next coherent extraction lane. | Fund structure and account service tests pass with no contract drift in `src/Meridian.Contracts`. |
| `Meridian.Documents` | Document attachments currently live inside evidence/reporting seams and need a clearer boundary only when document workflows deepen. | Evidence packet and report-pack retained-document tests prove no loss of lineage or manifest integrity. |

## Related Docs

- [Project Structure](project-structure.md)
- [Module Map](module-map.md)
- [Design Document Adaptation](design-document-adaptation.md)
- [Domain Boundaries](domains.md)
- [Layer Boundaries](layer-boundaries.md)
- [Meridian Design Document](../product/meridian-design-document.md)
