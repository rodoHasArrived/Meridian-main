# SFO MVP Implementation Design

**Last Reviewed:** 2026-05-29

## Purpose

This document defines the MVP scope for a single-family-office (SFO) operating lane in Meridian.
It is an implementation design note, not a new roadmap wave. It should be interpreted under the
current evidence-backed investment-operations direction in
[`current-direction-and-status.md`](current-direction-and-status.md), the differentiation and
archive rules in [`evidence-backed-investment-operations-plan.md`](evidence-backed-investment-operations-plan.md),
and the conservative readiness posture in [`../status/production-status.md`](../status/production-status.md).

The SFO MVP reuses Meridian's active operator navigation rather than creating a separate family-office
application. It packages existing Portfolio, Accounting, Reporting, Data, and Settings surfaces around
a family balance sheet, entity/account governance, private-asset evidence, capital activity, report
packs, and stakeholder access controls.

## MVP Outcome

The MVP should let an operator onboard one family office, represent family members and controlled
entities, connect accounts and private assets to ownership evidence, record capital activity, reconcile
supporting data/documents, and publish governed report packs to approved stakeholders. The success
criterion is a repeatable evidence chain from imported documents/provider feeds through holdings,
ledger activity, reconciliation status, and report-pack output.

## Core Concepts

| Concept | MVP definition | Primary workspace mapping | Evidence expectation |
| --- | --- | --- | --- |
| `FamilyOfficeEntity` | A legal or operating entity used by the family office, such as a trust, LLC, partnership, foundation, or investment vehicle. | **Accounting** owns entity setup, ledger scoping, capital-call context, and reconciliation views; **Portfolio** consumes the entity hierarchy for balance-sheet rollups. | Entity formation or onboarding documents, account-opening evidence, ledger references, and reconciliation decisions are retained as `EvidenceLink` records. |
| `FamilyMember` | A person, beneficiary, settlor, trustee, investment committee participant, or authorized stakeholder connected to the office. | **Settings** owns onboarding, role assignment, and permissions; **Reporting** consumes the membership model for stakeholder-room delivery. | Identity/onboarding approvals, role grants, and report-access decisions retain actor, timestamp, rationale, and source document links. |
| `OwnershipNode` | A graph node representing a family member, entity, account, private asset, or reporting group in the ownership structure. | **Accounting** owns the authoritative ownership graph; **Portfolio** reads the graph for exposure and balance-sheet rollups. | Each node includes provenance for its source, effective date, and current review status. |
| `OwnershipEdge` | A directed ownership, beneficial-interest, control, delegation, or reporting relationship between two ownership nodes. | **Accounting** owns edge maintenance and review; **Reporting** consumes approved edges to determine who can see which report-pack sections. | Each edge records source documents, effective dates, approval state, and exception/reconciliation notes when percentages or rights conflict. |
| `FamilyOfficeAccount` | A bank, brokerage, custodian, ledger, vehicle, or shadow-book account belonging to an entity or family-owned structure. | **Accounting** owns ledger/reconciliation posture; **Data** owns imported statements and provider/custodian feeds; **Portfolio** uses account holdings and balances. | Account statements, provider snapshots, balance history, reconciliation breaks, and ledger postings link back to durable evidence. |
| `PrivateAsset` | A non-public or manually valued asset such as real estate, operating-company interest, private fund commitment, direct loan, collectible, or custom asset profile. | **Portfolio** owns holdings, valuation, exposure, and family balance-sheet presentation; **Data** owns document/import lineage; **Accounting** owns ledger and capital activity impacts. | Valuation memos, capital statements, subscription documents, appraisal files, and override approvals are linked and reviewable. |
| `CapitalActivity` | A capital call, contribution, distribution, commitment change, fee, transfer, income event, or reallocation tied to an entity/account/private asset. | **Accounting** owns capital calls, ledger posting, and reconciliation; **Portfolio** reflects resulting exposure/cash changes. | Notices, payment confirmations, journal entries, and reconciliation outcomes are connected through `EvidenceLink`. |
| `FamilyReportPack` | A governed report bundle for family-office stakeholders, such as balance sheet, holdings, exposure, capital activity, exceptions, and evidence appendices. | **Reporting** owns report-pack lifecycle, stakeholder room, approvals, delivery status, and restatement handling. | Packs carry version, approval, source-data, exception, and export artifact evidence. |
| `StakeholderAccessPolicy` | The permission policy that determines which family members, advisors, trustees, or external stakeholders can view entities, accounts, private assets, capital activity, and report packs. | **Settings** owns roles, permissions, onboarding, and access review; **Reporting** enforces stakeholder-room visibility. | Access grants, denials, review attestations, and policy changes are auditable and link to approval evidence. |
| `EvidenceLink` | A typed reference from any SFO concept to a source document, provider feed, statement, import batch, ledger entry, reconciliation decision, approval, report artifact, or operator note. | **Data** owns import/document/provider-feed lineage; every workspace displays or consumes evidence relevant to its workflow. | Evidence links must be durable, timestamped, source-aware, and usable by report packs and reconciliation review. |

## Workspace Scope

### Portfolio

- Present a family balance sheet by family office, entity, ownership branch, account, and private asset.
- Show holdings, exposure, liquidity, concentration, and valuation freshness across public and private assets.
- Consume `OwnershipNode`, `OwnershipEdge`, `FamilyOfficeAccount`, `PrivateAsset`, `CapitalActivity`, and `EvidenceLink` data without owning duplicate governance logic.

### Accounting

- Maintain `FamilyOfficeEntity` records, the ownership graph, account-to-entity assignments, capital calls, ledger activity, and reconciliation posture.
- Record `CapitalActivity` against accounts, entities, and private assets with source documents and journal/reconciliation evidence.
- Surface exceptions where ownership percentages, account statements, ledger postings, or capital notices disagree.

### Reporting

- Build `FamilyReportPack` outputs from approved Portfolio, Accounting, and Data evidence.
- Provide a stakeholder room that respects `StakeholderAccessPolicy` rules for family members, trustees, advisors, and external reviewers.
- Retain report-pack versioning, approval status, export artifacts, restatement evidence, and exception disclosures.

### Data

- Import documents, statement files, custodian/provider feeds, valuation memos, capital notices, and other SFO support artifacts.
- Normalize feed/import lineage into `EvidenceLink` records that Portfolio, Accounting, and Reporting can display.
- Track document quality, missing evidence, stale valuation inputs, and provider/custodian feed freshness.

### Settings

- Configure family-office onboarding, member/advisor roles, permissions, stakeholder access policies, and review cadence.
- Manage `FamilyMember` setup, `StakeholderAccessPolicy` approvals, and access-review evidence.
- Keep root navigation aligned with the existing Meridian workspaces: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.

## MVP Boundaries And Sequencing

1. **Model first:** introduce shared contracts/read models for the ten core concepts, with `EvidenceLink` available from the first slice.
2. **Evidence before automation:** prioritize imports, documents, feed lineage, ledger references, and reconciliation posture before advanced workflow automation.
3. **Workspace reuse:** extend the active Portfolio, Accounting, Reporting, Data, and Settings surfaces rather than adding an SFO-only root navigation area.
4. **One golden path:** prove onboarding -> imports/documents -> account/private-asset setup -> capital activity -> reconciliation -> report pack -> stakeholder access review.
5. **Conservative readiness language:** SFO MVP status should remain planned/in-progress until the golden path has focused tests and operator acceptance evidence.

## Out Of Scope For MVP

- Legal advice or legal-document drafting.
- Tax advice, tax return preparation, or tax optimization recommendations.
- Native mobile apps, mobile-specific workflows, MAUI clients, React Native clients, Flutter clients, or iOS/Android applications.
- Full estate planning, estate simulation, or comprehensive trust/estate administration.
- Full bill payment, bank-payment initiation, treasury disbursement automation, or consumer banking workflows.
- Broad live-broker expansion beyond the provider/custodian feeds needed to prove SFO account, holdings, and statement evidence.

## Open Implementation Questions

- Which existing governance/fund-structure contracts should become the backing types for `FamilyOfficeEntity`, `OwnershipNode`, and `OwnershipEdge` versus receiving SFO-specific projections?
- Should `PrivateAsset` be implemented first through existing UFL/custom-asset profile projections, or through a narrower SFO-specific read model that can later converge with UFL?
- What minimum report-pack sections are required for stakeholder acceptance: balance sheet, holdings, capital activity, exceptions, evidence appendix, or all of them?
- Which fixtures should represent the first golden path: trust/LLC/brokerage account, private fund commitment, real-estate holding, or direct-lending private asset?

## Cross-References

- [`current-direction-and-status.md`](current-direction-and-status.md) remains the single planning entry point and claim-discipline source.
- [`evidence-backed-investment-operations-plan.md`](evidence-backed-investment-operations-plan.md) defines the product-category filter this MVP must satisfy.
- [`../status/production-status.md`](../status/production-status.md) defines current production/readiness posture and must remain conservative until SFO acceptance evidence exists.
