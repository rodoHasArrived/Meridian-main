# UFL Equity Target-State Package V2

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-20
**Status:** active reference design
**Reviewed:** 2026-05-13

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For equities: common share definition -> `ComShrDef`; preferred share definition -> `PrefShrDef`;
> convertible preferred -> `ConvPrefDef`; convertible common -> `ConvComDef`;
> voting/ownership trait -> `OwnTr`; dividend trait -> `DivTr`; redemption trait -> `RedTr`;
> callable trait -> `CallTr`; convertibility trait -> `ConvTr`;
> boolean fields -> `HasVoting: bool`, `IsRestricted: bool`, `IsCumulative: bool`,
> `IsCallable: bool`, `IsConvertible: bool`.

## Summary

This package defines the target-state equity surface for Meridian's UFL and Security Master work. It covers common equity, preferred equity, convertible equity, and convertible preferred equity.

Current repository evidence already includes equity classification support in `src/Meridian.FSharp/Domain/SecurityMaster.fs`, interop/legacy-upgrade handling, and shared reference-data endpoints for canonical equity reads. The remaining work is mostly projection depth, preferred/convertible lifecycle workflows, accounting/reporting handoffs, and controlled workstation actions.

Warrants are modeled as their own `SecurityKind.Warrant` package, not as an equity classification in this document.

## Repo Fit

### Verified Meridian constraints

- `SecurityKind.Equity`, `EquityTerms`, `PreferredTerms`, `ConvertibleTerms`, and `EquityClassification` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- Common equity remains valid when `EquityTerms.Classification` is omitted.
- Preferred and convertible-preferred shapes are covered by F# domain tests.
- Shared reference endpoints currently expose equity reference reads through `/api/reference-data/equities/*`.
- The browser workstation remains the active UI lane; retained WPF is compatibility support, not the place to introduce new equity UI surfaces first.

### Target-state additions

- equity lifecycle and alias-resolution projections
- preferred-term and convertible-term projection tables
- dividend schedule and conversion parity read models
- conversion, redemption, and call execution workflows with explicit audit events
- corporate-action accounting automation with preview, approval, journal posting, and reconciliation evidence
- Accounting and Reporting workstation views for term inspection and downstream impact review

### Suggested implementation locations

- F# domain support: `src/Meridian.FSharp/Domain/`
- application services: `src/Meridian.Application/SecurityMaster/`
- contracts/DTOs: `src/Meridian.Contracts/SecurityMaster/` or an equity-specific contract folder if the surface grows
- storage/projections: `src/Meridian.Storage/SecurityMaster/`
- HTTP endpoints: `src/Meridian.Ui.Shared/Endpoints/`

## Scope

### In scope

- canonical equity identity and common share-class metadata
- preferred equity terms, dividend cadence, redemption, callability, and liquidation preference
- convertible equity terms, underlying-security linkage, conversion ratio, and conversion windows
- full corporate-action accounting automation for equity lifecycle events
- projection rebuild safety for preferred and convertible reads
- reference-data and workstation query surfaces for equity lifecycle inspection

### Out of scope

- warrant lifecycle, which belongs in [UFL Warrant Target-State Package V2](ufl-warrant-target-state-v2.md)
- market-making, locate, borrow, margin, or short-sale workflows
- native mobile or mobile-first workflows

## Domain Shape

The current F# baseline separates common, preferred, convertible, and convertible-preferred classification:

```fsharp
type PreferredTerms = {
    DividendRate: decimal option
    DividendType: DividendType
    RedemptionPrice: decimal option
    RedemptionDate: DateOnly option
    CallableDate: DateOnly option
    ParticipationTerms: ParticipationTerms option
    LiquidationPreference: LiquidationPreference
}

type ConvertibleTerms = {
    UnderlyingSecurityId: SecurityId
    ConversionRatio: decimal
    ConversionPrice: decimal option
    ConversionStartDate: DateOnly option
    ConversionEndDate: DateOnly option
}

[<RequireQualifiedAccess>]
type EquityClassification =
    | Common
    | Preferred of PreferredTerms
    | Convertible of ConvertibleTerms
    | ConvertiblePreferred of PreferredTerms * ConvertibleTerms
    | Other of string

type EquityTerms = {
    ShareClass: string option
    VotingRightsCat: VotingRightsCat option
    Classification: EquityClassification option
}
```

## Corporate Action And Accounting Model

Full corporate-action accounting automation is in scope for equity target state. The target flow is controlled and evidence-backed:

1. Ingest or enter a corporate-action event with provider/source evidence, entitlement dates, affected security IDs, and economic terms.
2. Normalize the event into an equity corporate-action record tied to the canonical Security Master identity.
3. Produce an accounting-impact preview before any posting occurs.
4. Generate a balanced draft journal with `JournalEntry` metadata linking back to the action, source event, security, fund account, and approval chain.
5. Require Accounting workstation review for material or ambiguous actions.
6. Post approved journals, then expose Reporting and reconciliation evidence that ties the corporate-action event to ledger impact.

| Action | Core inputs | Accounting automation target | Required controls |
| --- | --- | --- | --- |
| Cash dividend | declaration, ex-date, record date, pay date, rate, withholding tax | dividend receivable, dividend income, tax withholding, cash settlement postings | accrual basis, tax classification, payable-date completeness |
| Stock dividend or split | ratio, effective date, cash-in-lieu rule | quantity and basis adjustment, cash-in-lieu posting when applicable | lot-level rebuild, no unbalanced cash movement |
| Return of capital | per-share amount, tax classification, pay date | cash receipt with cost-basis reduction and gain preview when basis is exhausted | basis availability, tax-lot evidence |
| Spin-off | parent security, child security, allocation factor, fair-value evidence | child security lot creation and basis allocation | child security identity, valuation source, lot allocation proof |
| Merger or exchange | old security, new security or cash consideration, exchange ratio | close old position, open new position or cash consideration, realized gain/loss preview | consideration completeness, fractional-share handling |
| Rights distribution | right terms, subscription price, expiration, underlying security | create right entitlement, allocate basis or fair value, expire/exercise accounting | linked rights or warrant identity, expiry controls |
| Preferred dividend | preferred terms, cumulative arrears, pay schedule | preferred dividend receivable/income and arrears state | cumulative arrears reconciliation |
| Conversion, redemption, or call | conversion ratio, redemption/call price, effective date | close or transform preferred/convertible position, cash/security consideration, gain/loss preview | eligibility window, operator approval, replay-safe execution |

Automation must not silently post journals from raw provider payloads. Every posting needs normalized action terms, deterministic impact calculation, and retained evidence for replay and audit.

## Read Models

### Core equity reference

- equity identity and display metadata
- issuer and primary listing venue
- share class and voting-right category
- lifecycle state for listed, suspended, delisted, inactive, or unknown status
- alias and provider-symbol resolution

### Preferred equity projections

- current preferred terms snapshot
- dividend schedule rows
- callable and redemption windows
- liquidation preference and participation metadata
- current-yield projection where price evidence is available

### Convertible equity projections

- underlying-security linkage
- conversion ratio and price
- conversion parity
- conversion eligibility windows
- conversion, redemption, and call execution history

### Accounting impact projections

- current corporate-action event state
- affected positions and tax lots
- draft journal entries and balance status
- realized/unrealized gain preview where applicable
- approval status, reviewer, and posting timestamp
- reconciliation status between source action, position impact, and ledger posting

## API Surface

### Implemented reference-data reads

- `GET /api/reference-data/equities/{securityId:guid}`
- `GET /api/reference-data/equities/by-exchange`
- `GET /api/reference-data/equities/by-issuer`

### Target-state preferred/convertible reads

- `GET /api/reference-data/equities/{securityId:guid}/preferred-terms`
- `GET /api/reference-data/equities/{securityId:guid}/dividend-schedule?fromDate=X&toDate=Y`
- `GET /api/reference-data/equities/{securityId:guid}/current-yield`
- `GET /api/reference-data/equities/{securityId:guid}/conversion-parity`
- `GET /api/reference-data/equities/{securityId:guid}/callable-windows`
- `GET /api/reference-data/equities/{securityId:guid}/redemption-terms`

### Target-state controlled actions

Mutations such as preferred-term amendments, conversion, redemption, and call execution should remain behind explicit Security Master or Accounting/Reporting workstation actions with audit metadata. They should not be treated as browser-navigable reference-data reads.

Target-state controlled endpoints:

- `POST /api/accounting/equities/{securityId:guid}/corporate-actions/{actionId}/preview`
- `POST /api/accounting/equities/{securityId:guid}/corporate-actions/{actionId}/approve`
- `POST /api/accounting/equities/{securityId:guid}/corporate-actions/{actionId}/post`
- `GET /api/accounting/equities/{securityId:guid}/corporate-actions/{actionId}/journal-draft`
- `GET /api/reporting/equities/{securityId:guid}/corporate-action-ledger-impact`

These routes are target-state API contracts. The currently implemented equity API surface is still limited to the reference-data reads listed above.

## Interfaces And Contracts

Existing implementation anchors:

- `IEquityReferenceService` in `src/Meridian.Application/Equity/`
- `EquityProjectionService` in `src/Meridian.Application/Equity/`
- `IEquityReferenceProjectionStore` in `src/Meridian.Storage/SecurityMaster/`
- `EquityReferenceDto` in `src/Meridian.Contracts/Equity/`
- `EquityReferenceEndpoints` in `src/Meridian.Ui.Shared/Endpoints/`

Target-state service boundaries:

```csharp
namespace Meridian.Application.Equity;

public interface IEquityCorporateActionAccountingService
{
    Task<EquityAccountingImpactPreviewDto> PreviewAsync(
        Guid securityId,
        Guid actionId,
        EquityAccountingImpactRequestDto request,
        CancellationToken ct = default);

    Task<EquityJournalDraftDto> CreateDraftJournalAsync(
        Guid securityId,
        Guid actionId,
        EquityJournalDraftRequestDto request,
        CancellationToken ct = default);

    Task<EquityCorporateActionPostingDto> PostApprovedJournalAsync(
        Guid securityId,
        Guid actionId,
        EquityJournalPostRequestDto request,
        CancellationToken ct = default);
}

public interface IEquityCorporateActionStore
{
    Task<EquityCorporateActionDto?> GetAsync(Guid actionId, CancellationToken ct = default);
    Task SaveAsync(EquityCorporateActionDto action, CancellationToken ct = default);
    Task SaveAccountingImpactAsync(EquityAccountingImpactPreviewDto impact, CancellationToken ct = default);
}
```

Suggested DTO families:

- `EquityCorporateActionDto`
- `EquityCorporateActionTermDto`
- `EquityAccountingImpactRequestDto`
- `EquityAccountingImpactPreviewDto`
- `EquityJournalDraftDto`
- `EquityJournalPostRequestDto`
- `EquityCorporateActionPostingDto`
- `EquityCorporateActionLedgerLinkDto`

## Storage Design

Additional projection tables for the preferred and convertible target state:

- `equity_corporate_action_event` - normalized action terms, source evidence, action status
- `equity_preferred_terms` - current preferred terms snapshot
- `equity_convertible_terms` - current convertible terms snapshot
- `equity_dividend_schedule` - projected dividend payments
- `equity_corporate_action_execution` - conversion, redemption, and call execution history
- `equity_accounting_impact_preview` - deterministic accounting impact before approval
- `equity_journal_draft` - balanced draft journal and validation state
- `equity_corporate_action_ledger_link` - final link between action, journal entry, fund account, and posting evidence

Index strategy:

- `equity_corporate_action_event`: `(security_id, effective_date)`, `(action_type, status)`, and `(source_system, source_event_id)`
- `equity_preferred_terms`: `(security_id, as_of)` for rebuild-safe slicing
- `equity_convertible_terms`: `(security_id, as_of)` and `(underlying_security_id)` for parity lookups
- `equity_dividend_schedule`: `(security_id, payment_date)` and `(ex_date)` for schedule queries
- `equity_corporate_action_execution`: `(security_id, executed_date)` for execution history
- `equity_accounting_impact_preview`: `(action_id, preview_version)` for repeatable review
- `equity_journal_draft`: `(action_id, approval_status)` for Accounting workstation queues
- `equity_corporate_action_ledger_link`: `(journal_entry_id)` and `(action_id, fund_account_id)` for reconciliation drill-ins

## Implementation Roadmap

### Delivered baseline evidence

1. Add `EquityClassification`, `PreferredTerms`, and `ConvertibleTerms` to the F# security-master domain.
2. Preserve common equity compatibility when classification is omitted.
3. Add interop/legacy-upgrade handling for current equity payloads.
4. Expose baseline equity reference-data reads.
5. Cover preferred and convertible-preferred shapes in deterministic domain tests.

### Remaining core equity work

1. Add equity trading-profile projection storage.
2. Add alias-resolution projection and rebuild path.
3. Extend `IEquityReferenceService` with lifecycle and alias queries.
4. Add lifecycle projection models for listed, suspended, delisted, inactive, and unknown states.
5. Add corporate-action import record storage.
6. Add rebuild orchestration for equity corporate actions.
7. Add Data workspace views for equity lifecycle and identity inspection.

### Remaining preferred and convertible work

1. Add preferred and convertible projection storage.
2. Implement dividend schedule projection builder.
3. Implement conversion parity projection builder.
4. Extend equity reference contracts with preferred and convertible read models.
5. Add preferred and convertible read endpoints.
6. Implement conversion execution workflow.
7. Implement redemption and call execution workflows.
8. Add deterministic tests for preferred and convertible flows.
9. Add Accounting and Reporting workstation review views for preferred lifecycle and downstream impact.

### Remaining corporate-action accounting automation

1. Add normalized `EquityCorporateActionDto` contracts and source-evidence metadata.
2. Add `IEquityCorporateActionStore` and replay-safe storage for normalized action terms.
3. Implement accounting-impact previews for dividends, splits, return of capital, spin-offs, mergers, rights, conversions, redemptions, and calls.
4. Generate balanced draft journals using the existing ledger model and validation path before posting.
5. Add approval state and reviewer metadata for material or ambiguous actions.
6. Link posted journals back to corporate-action events, affected lots, fund accounts, and source evidence.
7. Surface Accounting workstation queues for pending impact previews, draft journals, failed validations, and posted evidence.
8. Surface Reporting evidence for action-to-ledger lineage and restatement impact.
9. Add reconciliation checks that compare provider/source action facts, projected position impact, and ledger postings.

## Failure Modes And Controls

- Missing child security for spin-offs or mergers blocks posting and creates a Data workspace identity-resolution task.
- Ambiguous cash-in-lieu handling blocks posting until Accounting confirms fractional-share policy.
- Unbalanced draft journal blocks approval and must expose the debit/credit variance.
- Missing tax-lot basis blocks return-of-capital posting unless the operator explicitly approves a provisional basis workflow.
- Duplicate provider action IDs collapse into one normalized action record with source-evidence history, not multiple postings.
- Rebuilt projections must reproduce the same accounting-impact preview for the same normalized event version.
- Posted journals are immutable; correction flows create reversing and replacement entries linked to the original action.

## Test Plan

- Add domain tests for each corporate-action type's normalized terms and validation failures.
- Add service tests for `IEquityCorporateActionAccountingService` previews and balanced draft journals.
- Add storage tests proving replay/rebuild stability for `equity_corporate_action_event`, preview, draft, and ledger-link rows.
- Add endpoint tests for preview, approval, posting, and journal-draft routes once implemented.
- Add browser view-model tests for Accounting queues: loading, empty, validation-error, approval-required, posted, and correction states.
- Add reconciliation tests covering source action facts versus position impact versus ledger posting.

Initial focused validation once this implementation starts:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~EquityProjectionServiceTests|FullyQualifiedName~SecurityMasterConvertibleEquityAmendmentTests" --logger "console;verbosity=normal"
```

## Final Target State

Meridian treats every equity as a canonical instrument with explicit share-class, issuer, lifecycle, preferred-term, convertibility, and corporate-action accounting semantics. Data, Accounting, Reporting, ledger, and strategy consumers read one rebuilt equity reference surface and one retained action-to-ledger evidence trail instead of reinterpreting provider payloads independently.

## Related Documents

- [UFL Supported Asset Packages](ufl-supported-assets-index.md)
- [UFL Option Target-State Package V2](ufl-option-target-state-v2.md)
- [UFL Warrant Target-State Package V2](ufl-warrant-target-state-v2.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)
