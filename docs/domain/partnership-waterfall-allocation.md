# Partnership waterfall allocation

**Status:** active guidance
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-25

After a period closes its revenue and expense into retained earnings, a partnership allocates that
distributable profit to partners' capital through ordered waterfall tiers (for example a capped GP
catch-up, then a residual pro-rata split). This is a book allocation between equity accounts, not a cash
distribution; cash distributions use the separate distribution intake and `EuropeanDistributionWaterfall`.

## Queue an allocation for review

`POST /api/ledger/journal-automation/partnership-waterfall-intake` takes a
`RunPartnershipWaterfallDraftIntakeRequest`: the fund profile, the period, the distributable profit, the
partners, the ordered tiers (each with per-partner fractions summing to 1 and an optional cap), and the
retained-earnings scope the close used (`RetainedEarningsFinancialAccountId`, or none for the unscoped
`Retained Earnings` account).

`AutomatedJournalIntakeRunner.RunPartnershipWaterfallIntakeAsync` (`src/Meridian.Ui.Shared/Services/`):

1. Reads the period summary and confirms the ledger book belongs to the requested fund profile.
2. Requires the period to be **soft-closed**, so its closing entries have moved profit into retained
   earnings.
3. Refuses an allocation larger than the posted retained earnings in the named account, measured as credits
   less debits in the period summary's trial balance.
4. Projects the tiers with `PartnershipWaterfallProjector` against that account and wraps the result with
   `PartnershipWaterfallDraftBuilder` (kind `PartnershipProfitAllocated`).
5. Queues the draft as an **adjusting entry**, so it posts into the soft-closed period as an approved
   adjustment once reviewed. Nothing posts until the workbench lifecycle approves and posts it.

A refusal returns `409 Conflict` with the reason.

## What the journal contains

The journal debits retained earnings by the allocated profit and credits each partner's `Investor Capital`
account with that partner's tiered share. Profit beyond the last capped tier with no residual tier stays in
retained earnings and is reported as unallocated in the draft's tags. The idempotency key combines the fund,
the period and a digest of the inputs, so re-running the same terms queues nothing new.

On the partners' capital statement each partner's credit is reported as **allocated result**, because the
statement treats a retained-earnings counterpart as an income counterpart; the retained-earnings line shows
the offsetting movement, and ending capital still ties to ending equity.

## Current limits

- Two different allocations for the same period can both be queued; the cap is checked against posted
  retained earnings, so approval review must reject the one that should not post.
- The queued draft can be edited before approval like other automated drafts.
- There is no browser or desktop screen yet; the route is API-first.
