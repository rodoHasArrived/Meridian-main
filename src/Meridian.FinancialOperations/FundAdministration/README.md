# Fund Administration Controls

FundStudio-style administration controls that drive organization / entity / portfolio / account /
book / period / report administration. This area is a thin, in-memory control plane that composes the
ledger-domain primitives in `Meridian.Ledger` and records every privileged action into a
tamper-evident, append-only governance log.

## Surface

`FundAdministrationControlService` is the entry point. It drives:

- **Multi-book locked periods** with an **evidence-bearing reopen** path
  (`LockedAccountingPeriodBook`, `PeriodReopenEvidence`).
- **Journal templates** and **recurring journals** — parameterized, reusable posting templates
  (`JournalTemplate`, `JournalTemplateBook`) materialized on a cadence (`RecurringJournalSchedule`,
  `RecurringJournalPlanner`), lock-aware so occurrences inside a locked period are held back.
- **Year-end close** with a readiness gate over constituent periods and a retained-earnings
  roll-forward (`YearEndCloseProjector`).
- **Portfolio-specific pricing rules** that select price source / valuation method per portfolio and
  instrument (`PortfolioPricingRuleBook`), complementing the fund-scoped
  `DailyPortfolioPricingPolicy`.
- **Onboarding templates** — reusable blueprints that stamp out the standard fund structure
  (`OnboardingTemplate`, `OnboardingPlan`).

## Immutable governance log

Every posting, lock, reopen, export, and delivery event is appended to a
`Meridian.Ledger.FundAdministrationEventLog` — an append-only, SHA-256 hash-chained log with
`VerifyIntegrity()` for tamper detection. It mirrors the proven contract of
`Meridian.Audit.Compliance.ImmutableAuditLogService` but is a pure-domain primitive with a
fund-administration vocabulary, so it can be shared with the middle-office workflow service to keep
all governance events in one chain.

## Relationship to existing systems

- Journal templates here are the posting engine; the operator/API editing shape remains
  `Meridian.Contracts.Ledger.JournalEntryTemplateDto`.
- Onboarding plans are consumed by the existing fund-structure services
  (`IFundStructureService`, `FundStructureSetupWorkflowService`) to create the actual nodes.
- State is held in-memory behind a single lock, matching the repository's in-memory service
  convention; a durable store can wrap the same seams.
