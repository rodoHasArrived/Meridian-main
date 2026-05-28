# UFL Supported Asset Packages

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-28

## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **ufl supported assets index** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the ufl supported assets index workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

**Status:** active reference index
**Reviewed:** 2026-05-13

## Summary

This index is the active entry point for UFL capability profiles and target-state packages. It groups the security-master asset classes Meridian models in `src/Meridian.FSharp/Domain/SecurityMaster.fs`, maps through `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`, and exposes through shared workstation/reference-data surfaces as each slice matures.

The existing direct-lending package remains the deepest vertical slice. The sibling packages below are active asset profiles for Security Master, ledger, Accounting, Reporting, Data, and controlled workstation workflows. They are not milestone-closure documents; each package separates delivered baseline support from target-state additions that still need implementation evidence.

UFL should be read as a shared capability and conformance framework first. Individual asset packages are thin delta documents over the canonical [UFL Capability Model](ufl-capability-model.md), maturity levels, projection/evidence kernel, and milestone contracts.

## Current Evidence Boundary

- Direct lending is the deepest UFL vertical slice and still owns the dedicated implementation roadmap.
- Reference-data endpoint support currently exists for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit.
- Commercial paper, treasury bill, repo, cash sweep, other-security, CFD, and warrant packages remain active target-state designs unless their individual checklist marks a narrower baseline as delivered.
- Do not treat a target-state package as complete just because a `SecurityKind` case, CSV parser mapping, or basic projection exists.

## UFL Maturity Model

| Level | Name | Meaning |
| ---: | --- | --- |
| L0 | Cataloged | Asset appears in roadmap or docs only. |
| L1 | Canonical Terms | `SecurityKind`, terms, mapping, and validation exist. |
| L2 | Reference Read | Stable DTOs or endpoints expose canonical reference data. |
| L3 | Projection Safe | Rebuildable projections, checkpoints, lineage, and replay tests exist. |
| L4 | Operational Workflow | Operator actions, approval/review, correction, and audit trail exist. |
| L5 | Accounting/Reconciliation Integrated | Journals, period controls, reconciliation, and reporting evidence exist. |

Use the [UFL Conformance Matrix](ufl-conformance-matrix.md) to track current and next maturity by asset. Use `partial` instead of rounding up when evidence is mixed.

## Architectural Lanes

| Lane | Owner | Includes |
| --- | --- | --- |
| Lane A - UFL Reference Kernel | Security Master | canonical identity, terms, aliases, issuer/counterparty, underlying links, validation, read APIs |
| Lane B - UFL Projection and Evidence Kernel | Shared application/storage infrastructure | rebuild orchestration, checkpoints, event lineage, provenance, replay-safe read models |
| Lane C - Asset-Specific Operations | Vertical modules when needed | servicing, corporate-action accounting, repo exposure, swap references, option chain lifecycle, fixed-income extensions |

## Provider Payload Boundary

Provider payloads may be retained as evidence, import source, and troubleshooting context. Downstream UFL workflows must consume canonical Security Master identities, canonical terms, canonical aliases, and canonical projections, not raw provider payloads.

## Foundation Documents

| Document | Role |
| --- | --- |
| [UFL Capability Model](ufl-capability-model.md) | Capability set, maturity levels, lanes, and required asset-profile sections. |
| [UFL Conformance Matrix](ufl-conformance-matrix.md) | Single planning view of current maturity, next level, gaps, and evidence needed. |
| [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md) | Shared projection metadata, rebuild, checkpoint, lineage, and provider-isolation target. |
| [UFL Accounting Impact Model](ufl-accounting-impact-model.md) | Shared accounting/reconciliation capability boundary and L5 milestone pattern. |
| [UFL Asset Profile Template](ufl-asset-profile-template.md) | Required structure for converted asset package documents. |

## Naming Standard

All new F# types and C# DTOs proposed in these packages must follow the
[Meridian Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).

**Key rules for type names proposed in UFL packages:**

| Concept | Required pattern | Examples |
|---|---|---|
| New identifier types | `XxxId` single-case DU | `CorpActId`, `OptChainId` |
| Instrument definition records | `XxxDef` | `BondDef`, `EquityDef`, `OptDef`, `FutDef`, `FxDef` |
| Trait records (cross-cutting) | `XxxTr` | `OwnTr`, `IncTr`, `ConvTr`, `RedTr`, `SenTr` |
| Link / join records | `LeftRightLnk` | `SecIssLnk`, `SecExchLnk`, `CorpActSecLnk` |
| Status discriminated unions | `XxxStat` | `CorpActStat`, `SecurityStat` |
| Boolean fields | `Is`/`Has` prefix | `IsCallable`, `HasVoting`, `IsBullet` |
| Date fields (new F# code) | `Dt` suffix | `MaturityDt`, `IssueDt`, `ExpiryDt` |

## Custom Asset Composability

UFL should include a user-configurable custom-asset lane for repeatable instruments that do not yet justify a compiled asset package. The custom lane must be composable, governed, versioned, and promotion-ready: users configure approved capability profiles and typed fields, while Meridian preserves canonical Security Master identity, lineage, validation, projection rebuild safety, and review controls. See [UFL Custom Asset Composability](ufl-custom-asset-composability.md).

Custom assets are not a bypass around modeling discipline. One-off generic instruments can still use `OtherSecurity`, but repeated profile-backed instruments should be reviewed for promotion into dedicated packages when their usage becomes operationally important.

## Asset Packages

| Group | Packages |
| --- | --- |
| Foundation | [Capability Model](ufl-capability-model.md), [Conformance Matrix](ufl-conformance-matrix.md), [Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md), [Accounting Impact Model](ufl-accounting-impact-model.md), [Asset Profile Template](ufl-asset-profile-template.md) |
| Deep vertical slice | [Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md), [Direct Lending Implementation Roadmap](ufl-direct-lending-implementation-roadmap.md) |
| Listed and derivative instruments | [Equity Target-State Package V2](ufl-equity-target-state-v2.md), [Option Target-State Package V2](ufl-option-target-state-v2.md), [Future Target-State Package V2](ufl-future-target-state-v2.md), [Warrant Target-State Package V2](ufl-warrant-target-state-v2.md), [CFD Target-State Package V2](ufl-cfd-target-state-v2.md), [Swap Target-State Package V2](ufl-swap-target-state-v2.md) |
| Rates, cash, and credit | [Bond Target-State Package V2](ufl-bond-target-state-v2.md), [Treasury Bill Target-State Package V2](ufl-treasury-bill-target-state-v2.md), [Commercial Paper Target-State Package V2](ufl-commercial-paper-target-state-v2.md), [Certificate of Deposit Target-State Package V2](ufl-certificate-of-deposit-target-state-v2.md), [Deposit Target-State Package V2](ufl-deposit-target-state-v2.md), [Cash Sweep Target-State Package V2](ufl-cash-sweep-target-state-v2.md), [Money Market Fund Target-State Package V2](ufl-money-market-fund-target-state-v2.md), [Repo Target-State Package V2](ufl-repo-target-state-v2.md) |
| Other asset coverage | [FX Spot Target-State Package V2](ufl-fx-spot-target-state-v2.md), [Commodity Target-State Package V2](ufl-commodity-target-state-v2.md), [Crypto Target-State Package V2](ufl-crypto-target-state-v2.md), [Other Security Target-State Package V2](ufl-other-security-target-state-v2.md) |

## Notes

- These packages are intentionally grounded in Meridian's current `SecurityKind` union and validation rules.
- Where a package proposes new projections, services, or endpoints, those are target-state additions unless current code evidence is named in the package.
- The direct-lending document stays authoritative for the deepest fund-ops specialization; the others are thinner implementation-ready companion blueprints.
- Keep UI references aligned with the current browser workstation workspaces: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
