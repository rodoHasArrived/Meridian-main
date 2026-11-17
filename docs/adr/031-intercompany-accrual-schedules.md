# ADR-031: Opt-in, Reviewed Intercompany Accrual Schedules

**Status:** Proposed; first increment implemented
**Date:** 2026-11-17
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-17
**Deciders:** Repository owner (scope direction recorded as `DEC-IC-SCHEDULE-001`)
**Supersedes:** None. It narrows ADR-028's "nothing runs on a timer" boundary for one opt-in case.
**Superseded by:** None

## Context

Governed intercompany loan interest (ADR-028) is drafted only when an operator requests each window. Month-end
therefore depends on someone remembering every loan in every book, and a missed period is noticed only as an
aged intercompany break. Running accruals automatically for every profile would change behaviour for books that
never asked for it and would create drafts nobody reviewed the decision to create.

## Decision

Meridian drafts governed loan-interest accruals on a schedule **only** where a reviewed schedule says so.

1. **A schedule is reviewed configuration.** It names one ledger book, one approved `LoanInterest` profile and its
   loan principal profile. One operator proposes it with a reason; someone else approves it. It can be rejected
   or, once active, disabled, each with a reason. Versions are append-only (ledger migration `075`, or an atomic
   file store), and there is at most one proposed or active schedule per book and profile.
2. **A run drafts, nothing more.** For each completed period of the loan's declared schedule that ends on or after
   the approval date, a run previews and drafts through exactly the path an operator uses, binding the draft to
   the previewed evidence digest. The draft records `accrual-schedule:{id}` as its preparer and enters ordinary
   journal review; nothing is approved or posted.
3. **Existing drafts are left alone.** A period whose draft already exists, in any status, is reported and skipped;
   a run never recomputes or overwrites it. A refused period (missing fixing, compounding coverage, closed period,
   posted overlap) is reported with the server's reason and retried on the next run.
4. **Bounded runs.** A background runner checks active schedules every six hours after a one-minute start-up delay,
   drafting at most 24 periods per schedule per run, oldest first. A failing schedule never stops the others.
   Operators can preview the due periods of any schedule and run an active one on demand.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Contracts | `src/Meridian.Contracts/Ledger/IntercompanyAccrualScheduleDtos.cs` | Schedule, requests, run result and store interface |
| Rules | `src/Meridian.FinancialOperations/Intercompany/IntercompanyAccrualScheduleRules.cs` | Maker-checker transitions |
| Stores | `src/Meridian.Storage/Ledger/PostgresIntercompanyAccrualScheduleStore.cs`, `FileIntercompanyAccrualScheduleStore.cs`, `Migrations/V_ledger_075__intercompany_accrual_schedules.sql` | Append-only versions |
| Service and runner | `src/Meridian.Ui.Shared/Services/IntercompanyAccrualScheduleService.cs`, `IntercompanyAccrualScheduleRunner.cs` | Due periods, runs, background schedule |
| Routes | `src/Meridian.Ui.Shared/Endpoints/IntercompanyAccrualScheduleEndpoints.cs` | `/api/ledger/intercompany/accrual-schedules` |
| Surfaces | Browser Accrual schedules tab; WPF Accrual schedules tab | Propose, review, preview due periods, run now |
| Tests | `tests/Meridian.Tests/Storage/IntercompanyAccrualScheduleStoreTests.cs`, `tests/Meridian.Tests/Ui/IntercompanyAccrualScheduleServiceTests.cs`, `IntercompanyAccrualScheduleEndpointTests.cs` | Rules, stores (file and PostgreSQL), runs, runner and routes |

## Alternatives Considered

### Alternative 1: Accrue every approved profile automatically

**Pros:** no configuration.
**Cons:** books that never asked for automatic drafts would receive them; there is no reviewed decision to point to.
**Why rejected:** automation of a governed path is itself a decision that needs review.

### Alternative 2: Schedules in host configuration

**Pros:** no new store or screens.
**Cons:** schedules would live outside the reviewed, tenant-scoped audit trail.
**Why rejected:** the instruction to draft must be as traceable as the drafts it creates.

## Consequences

- A period is drafted only once; a corrected period is redrafted by an operator after reversal, as before.
- Management-fee schedules follow once governed fee drafting is composed on main.

## References

- [ADR-028: Governed Intercompany Loan-Interest Accrual](028-governed-intercompany-interest-accrual.md)
- [Intercompany agreements](../domain/intercompany-agreements.md)
