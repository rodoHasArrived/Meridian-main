# UFL Supported Asset Packages

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-13
**Status:** active reference index
**Reviewed:** 2026-05-13

## Summary

This index is the active entry point for UFL target-state packages. It groups the security-master asset classes Meridian models in `src/Meridian.FSharp/Domain/SecurityMaster.fs`, maps through `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`, and exposes through shared workstation/reference-data surfaces as each slice matures.

The existing direct-lending package remains the deepest vertical slice. The sibling packages below are active reference designs for Security Master, ledger, Accounting, Reporting, Data, and controlled workstation workflows. They are not milestone-closure documents; each package separates delivered baseline support from target-state additions that still need implementation evidence.

## Current Evidence Boundary

- Direct lending is the deepest UFL vertical slice and still owns the dedicated implementation roadmap.
- Reference-data endpoint support currently exists for bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, and certificates of deposit.
- Commercial paper, treasury bill, repo, cash sweep, other-security, CFD, and warrant packages remain active target-state designs unless their individual checklist marks a narrower baseline as delivered.
- Do not treat a target-state package as complete just because a `SecurityKind` case, CSV parser mapping, or basic projection exists.

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

## Asset Packages

| Group | Packages |
| --- | --- |
| Deep vertical slice | [Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md), [Direct Lending Implementation Roadmap](ufl-direct-lending-implementation-roadmap.md) |
| Listed and derivative instruments | [Equity Target-State Package V2](ufl-equity-target-state-v2.md), [Option Target-State Package V2](ufl-option-target-state-v2.md), [Future Target-State Package V2](ufl-future-target-state-v2.md), [Warrant Target-State Package V2](ufl-warrant-target-state-v2.md), [CFD Target-State Package V2](ufl-cfd-target-state-v2.md), [Swap Target-State Package V2](ufl-swap-target-state-v2.md) |
| Rates, cash, and credit | [Bond Target-State Package V2](ufl-bond-target-state-v2.md), [Treasury Bill Target-State Package V2](ufl-treasury-bill-target-state-v2.md), [Commercial Paper Target-State Package V2](ufl-commercial-paper-target-state-v2.md), [Certificate of Deposit Target-State Package V2](ufl-certificate-of-deposit-target-state-v2.md), [Deposit Target-State Package V2](ufl-deposit-target-state-v2.md), [Cash Sweep Target-State Package V2](ufl-cash-sweep-target-state-v2.md), [Money Market Fund Target-State Package V2](ufl-money-market-fund-target-state-v2.md), [Repo Target-State Package V2](ufl-repo-target-state-v2.md) |
| Other asset coverage | [FX Spot Target-State Package V2](ufl-fx-spot-target-state-v2.md), [Commodity Target-State Package V2](ufl-commodity-target-state-v2.md), [Crypto Target-State Package V2](ufl-crypto-target-state-v2.md), [Other Security Target-State Package V2](ufl-other-security-target-state-v2.md) |

## Notes

- These packages are intentionally grounded in Meridian's current `SecurityKind` union and validation rules.
- Where a package proposes new projections, services, or endpoints, those are target-state additions unless current code evidence is named in the package.
- The direct-lending document stays authoritative for the deepest fund-ops specialization; the others are thinner implementation-ready companion blueprints.
- Keep UI references aligned with the current browser workstation workspaces: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
