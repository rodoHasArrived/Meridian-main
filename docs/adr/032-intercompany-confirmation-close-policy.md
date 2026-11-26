# ADR-032: Opt-in, Policy-Governed Close Blocking from Intercompany Balance Confirmation

**Status:** Proposed; first increment implemented
**Date:** 2026-11-18
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-18
**Deciders:** Repository owner (pending acceptance of `DEC-IC-CONFIRM-002`)
**Supersedes:** None. It extends `DEC-IC-CONFIRM-001`, which kept confirmation advisory.
**Superseded by:** None

## Context

Reciprocal balance confirmation (`W12-ICCONFIRM-001`) reports `intercompany.confirmation.*` findings in the close
advisory, which close readiness attaches after it has decided eligibility. A controller who wants month-end close to
wait for a disputed pair to be resolved has no way to say so. Turning every finding into a blocker for every book would
change close for books that never asked for it, and `DEC-IC-CONFIRM-001` requires a separate decision for any blocking.

## Decision

A ledger book may adopt **one reviewed confirmation close policy**. Without one, close readiness is unchanged.

1. **A policy is reviewed configuration.** It names one ledger book and the confirmation findings that block close,
   chosen from `not-proposed`, `awaiting-confirmation`, `disputed`, `balances-changed` and
   `resolved-awaiting-proposal`. One operator proposes it with a reason; someone else approves it. It can be rejected
   or, once active, disabled, each with a reason. Versions are append-only (ledger migration `078`, or an atomic file
   store). A book has at most one proposed or active policy.
2. **An active policy is one close-readiness contributor.** `IntercompanyConfirmationCloseGate` reads the confirmation
   position at the selected period end. A listed finding makes the `intercompany-confirmation` contributor `Blocked`,
   naming the policy, version, finding and accounting profile. No listed finding makes it `Ready`.
3. **It fails closed.** With an active policy, a missing period, confirmation balances that cannot be read, a host
   without the confirmation service, or more than one active policy make the contributor `Unavailable`, so the close
   decision is incomplete rather than ready.
4. **The advisory is unchanged.** Every confirmation finding still appears as a warning in the close advisory, which
   never changes eligibility. The policy is the only path from confirmation to a blocker.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Contracts | `src/Meridian.Contracts/Ledger/IntercompanyConfirmationClosePolicyDtos.cs`, `src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs` | Policy versions, requests, store and gate interfaces |
| Rules and service | `src/Meridian.FinancialOperations/Intercompany/IntercompanyConfirmationClosePolicyRules.cs`, `src/Meridian.FinancialOperations/Intercompany/IntercompanyConfirmationClosePolicyService.cs` | Maker-checker transitions and evaluation |
| Close gate | `src/Meridian.FinancialOperations/Intercompany/IntercompanyConfirmationCloseGate.cs`, `src/Meridian.FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.CloseInputs.cs` | The `intercompany-confirmation` contributor |
| Stores | `src/Meridian.Storage/Ledger/PostgresIntercompanyConfirmationClosePolicyStore.cs`, `src/Meridian.Storage/Ledger/FileIntercompanyConfirmationClosePolicyStore.cs`, `src/Meridian.Storage/Ledger/Migrations/V_ledger_078__intercompany_confirmation_close_policies.sql` | Append-only versions |
| Routes | `src/Meridian.Ui.Shared/Endpoints/IntercompanyConfirmationClosePolicyEndpoints.cs` | `/api/ledger/intercompany/confirmation-close-policies` list, propose and decide |
| Browser | `src/Meridian.Ui/dashboard/src/screens/accounting-screen.entity-confirmation-close-policy.tsx` | Policy panel on the balance-confirmation screen |
| Desktop | `src/Meridian.Ui.Services/Services/Accounting/IntercompanyBalanceConfirmationWorkspace.ClosePolicy.cs`, `src/Meridian.Wpf/Views/IntercompanyBalanceConfirmationControl.xaml` | Policy section on the Balance confirmation tab |
| Tests | `tests/Meridian.Tests/Storage/IntercompanyConfirmationClosePolicyTests.cs`, `tests/Meridian.Tests/FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.ConfirmationClosePolicyTests.cs`, `tests/Meridian.Tests/Ui/IntercompanyConfirmationClosePolicyEndpointTests.cs` | Rules, gate, stores (file and PostgreSQL), contributor and routes |

Implemented on 2026-11-18 with synthetic fixtures. Acceptance of `DEC-IC-CONFIRM-002` by the repository owner is
pending; until then the capability exists but no book is expected to adopt a policy in production.

## Rationale

A per-book, reviewed policy keeps the default exactly as today and puts the decision to block close with the people
accountable for that book's close. Reusing the close-readiness contributor seam means the blocker appears, is
explained and is evidenced like every other close requirement, with no special case in either workstation.

## Alternatives Considered

### Alternative 1: Promote confirmation warnings to blockers globally

Rejected: it changes close for every book at once without anyone choosing it, which `DEC-IC-CONFIRM-001` excluded.

### Alternative 2: A tenant-wide policy

Deferred: books in one tenant close on different calendars and with different counterparties. A tenant default can be
added later without changing the per-book contributor.

## Consequences

- Books without a policy see no change.
- A book with an active policy cannot close while a listed finding is open or while confirmation cannot be read.
- Disabling the policy restores advisory-only confirmation for later evaluations.
