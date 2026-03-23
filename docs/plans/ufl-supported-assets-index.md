# UFL Supported Asset Packages

**Owner:** Core Team  
**Audience:** Product, architecture, domain, storage, and application contributors  
**Last Updated:** 2026-03-22  
**Status:** active  
**Reviewed:** 2026-03-22

## Summary

This index groups the active UFL target-state packages for the security-master asset classes Meridian currently models in `src/Meridian.FSharp/Domain/SecurityMaster.fs` and maps in `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`.

The existing direct-lending package remains the deepest vertical slice. The sibling packages below cover the other supported asset classes so architecture, storage, workflow, and validation decisions can be tracked consistently across the full security-master surface.

## Asset Packages

- [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md)
- [UFL Equity Target-State Package V2](ufl-equity-target-state-v2.md)
- [UFL Option Target-State Package V2](ufl-option-target-state-v2.md)
- [UFL Future Target-State Package V2](ufl-future-target-state-v2.md)
- [UFL Bond Target-State Package V2](ufl-bond-target-state-v2.md)
- [UFL FX Spot Target-State Package V2](ufl-fx-spot-target-state-v2.md)
- [UFL Deposit Target-State Package V2](ufl-deposit-target-state-v2.md)
- [UFL Money Market Fund Target-State Package V2](ufl-money-market-fund-target-state-v2.md)
- [UFL Certificate of Deposit Target-State Package V2](ufl-certificate-of-deposit-target-state-v2.md)
- [UFL Commercial Paper Target-State Package V2](ufl-commercial-paper-target-state-v2.md)
- [UFL Treasury Bill Target-State Package V2](ufl-treasury-bill-target-state-v2.md)
- [UFL Repo Target-State Package V2](ufl-repo-target-state-v2.md)
- [UFL Cash Sweep Target-State Package V2](ufl-cash-sweep-target-state-v2.md)
- [UFL Other Security Target-State Package V2](ufl-other-security-target-state-v2.md)
- [UFL Swap Target-State Package V2](ufl-swap-target-state-v2.md)

## Notes

- These packages are intentionally grounded in Meridian's current `SecurityKind` union and validation rules.
- Where a package proposes new projections, services, or endpoints, those are target-state additions rather than already-implemented components.
- The direct-lending document stays authoritative for the deepest governance and fund-ops specialization; the others are thinner but implementation-ready companion blueprints.
