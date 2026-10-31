# Security

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-10-30

## Definition

A Security represents a financial instrument, investable asset, or fund interest that can appear in positions, transactions, valuations, reconciliations, and reports.

## Relationships

- Belongs to or references an issuer, obligor, counterparty, or fund sponsor when applicable.
- Can have positions across multiple portfolios.
- Can be referenced by transactions, prices, corporate actions, expected cash flows, and reconciliation evidence.
- Can flow into accounting events when valuation, income, realized gain/loss, or capital allocation rules require it.

## Business Rules

- A Security must have stable identity separate from provider-specific symbols.
- Provider symbols, CUSIPs, ISINs, tickers, and local IDs are identifiers for matching, not the canonical security itself.
- Security type drives required attributes, valuation rules, and reporting treatment.
- Security master changes that affect accounting or reconciliation require retained evidence and review state.

## Examples

- Public equity share
- Bond
- Private loan
- Derivative contract
- Fund interest

## Future Expansion Notes

Future additions should support richer issuer hierarchies, multi-identifier matching, corporate actions, expected cash-flow models, private-asset attributes, and fund-interest look-through behavior without coupling provider ingestion directly to accounting records.

## Price and projected-cash-flow evidence

A raw price observation has security, source, effective timestamp, recorded timestamp and explicit
quote units. Golden-copy selection uses both economic and knowledge cutoffs with a retained
hierarchy version. An untyped legacy quote or an assumed class-level par value is insufficient
valuation evidence.

An absent coupon or fixing is unresolved economics, distinct from contractual zero. A normalized
per-100 schedule is analysis only until actual principal/notional is retained. Bills and discount
commercial paper pay principal rather than coupon interest; carrying-value accretion is separate.

Price-selection receipts retain an immutable result, hierarchy and comparison snapshot under
security/account identity. Replay by receipt preserves what was actually evaluated even when a
transaction with an earlier recorded timestamp commits later. Timestamp cutoffs alone are
eligibility predicates, not durable certification or a commit-consistent snapshot.

`GetRecordedByIdentifierAsOfAsync` applies that same timestamp limitation to retained Security
Master event state and alias revisions. Its recorded cutoff filters stored timestamps; the separate
effective cutoff selects identifier validity. Event timestamps and alias transaction timestamps can
precede lock acquisition and commit. A later commit can therefore change a repeated query with the
same cutoffs. Imported alias baselines also have a migration availability eligibility boundary;
earlier alias contents remain unknown. This identifier API retains no evaluation receipt and makes
no claim about which transactions were visible at a historical cutoff.

## Governed edit concurrency

PostgreSQL-backed workbench edits use a durable per-security generation, separate from the canonical
economic event-stream version. Migration `036_security_master_workbench_generations.sql` creates
that fence even when no overlay exists. A command reads the generation, advances it with a conditional
update, validates against the current overlay, and commits the overlay and draft together. A concurrent
command that read the predecessor generation gets a conflict; there is no automatic retry of stale
validation or review assumptions. Reload and explicitly retry so validation runs against the winner.
The canonical stream version is still checked under its existing stream lock before overlay mutation.

Approval scans and decisions, guarded generic patches, submission, discard, and the final
approval-to-published transition use the same database boundary. Revision-store calls outside the
workbench also participate. The stores must share one configured database and schema; mixed durable
and in-memory participants are rejected. PostgreSQL instances do not rely on the process-local gate.
A failed draft insert or cancellation rolls back values, review audit, draft and generation together.
Standalone edit provenance remains best-effort after commit; provenance written during discard joins
the transaction. Optional discard provenance and decision convergence run behind savepoints: a caught
SQL error rolls back only the optional step, leaving the core rejection and overlay change committable.
Savepoint-management failures propagate and roll back the command. The generation fence uses the
same unquoted-schema case folding as the migration runner. Parallel read-model composition serializes
commands on the enlisted connection.

This does not make approval workflow persistence or downstream publish handlers part of a distributed
transaction. Their existing reconciliation and idempotent retry contracts still apply. It does not
implement payload-family, effective-history or shared-economics changes; dated schedule validation
is described separately below.

Deployment requires human schema/storage review and a coordinated writer rollout: drain old writer
instances, apply migration 036 through the normal migration runner, then start the updated instances.
Mixed old/new writers are not covered, because old binaries do not acquire the generation fence.
The additive table can remain during a binary rollback, but the multi-instance guarantee is then lost.
No production migration or operator acceptance is implied by this change.
Use the [governed edit rollout procedure](../operators/security-master-governed-edit-rollout.md)
to collect the independent review, writer-drain, migration, reconciliation and operator-acceptance
receipts. Missing production evidence stays pending even after merge and successful CI.

`SecurityMasterGovernedConcurrencyPostgresTests` forces two independent services to overlap at the
PostgreSQL lock, then verifies incompatible-date rejection, approval exclusion during patch/draft
creation, SQL-failure rollback, cancellation rollback, concurrent reads and composition guards.
The service-backed GitHub Actions integration lane is the required database proof; skipped local
PostgreSQL tests do not satisfy it.

## Dated economic schedule validation

`SecurityEconomicScheduleParser` is the common boundary for factor history, coupon steps and
contractual principal payments. The cash-flow resolver, schema validation, whole-schedule field
edits, canonical mapping and PostgreSQL projections consume its typed results and diagnostics.
This validates supplied economic rows; it does not change the contractual period-generation
kernel or the versioned call/put and schedule-convention rules.

| Schedule | Accepted row rules | Accepted calculation/projection values |
| --- | --- | --- |
| Factor history | Parseable date, factor in `[0, 1]`, unique dates, non-increasing factors in chronological order | Date-sorted factor points |
| Coupon steps | Parseable date, nonnegative rate, unique dates | Date-sorted coupon steps |
| Principal payments | Parseable date, strictly positive amount, exactly representable same-date total | Same-date amounts summed exactly, then date-sorted |

An absent or JSON-null schedule allows the next source to be considered. An explicit empty array
claims ownership and prevents fallback. A non-null malformed selected declaration also claims
ownership but fails with diagnostics; it never becomes an empty schedule or falls through to lower
authority. Source priority remains `profileFields`, outer asset terms, then common terms. Container
and row aliases are case-insensitive, and valid legacy decimal strings and date/timestamp values
remain readable. The free-text `factorSchedule` field stays an operational evidence reference;
its legacy array form is arithmetic, while `factorScheduleEntries` has first alias priority.

The result distinguishes absent, empty, populated and invalid schedules. Invalid results expose no
partial entries. Stable `SM_SCHEDULE_*` diagnostics identify the selected source, actual member
spelling and original zero-based row index, such as
`assetSpecificTerms.profileFields.factorScheduleEntries[1].factor`. Duplicate factor and step dates
are refused even when values match; input order never selects economic truth. Repeated spellings
of a selected JSON member are also refused before JSONB can discard or reorder them; distinct
aliases keep their documented priority. Required row members are checked on retained reads and
partial submissions of whole schedules alike. A class schema can separately require an explicit
schedule assertion (DirectLoan principal payments); a partial submitted-field update can omit it.

Numeric values must be exactly representable as CLR decimals. Parsing must not round an out-of-range
factor into range or a small negative rate to zero. Insignificant trailing zeros and exactly
representable JSON exponent notation remain valid. Principal aggregation accumulates the complete
same-date total exactly before converting it to a decimal, so row order cannot lose a small payment
or change acceptance. A total outside the decimal range yields `SM_SCHEDULE_SUM_OVERFLOW`; an
in-range total requiring rounding yields `SM_SCHEDULE_SUM_PRECISION`. Both identify the last
contributing source amount and expose no partial schedule.

Calculations and relational child tables use normalized entries. Domain codecs use the same valid
typed rows in their original order, retaining separate same-date principal rows for historical
round trips. Canonical create/read/amend mapping carries selected common-term schedules into the
typed kind, including a common-only schedule amendment, so a later revision does not discard them.
Parsing does not rewrite stored JSON, change version stamps, filter dates relative to
today or reconstruct missing historical terms. Canonical commands retain contextual checks such as
maturity windows and principal totals versus par. Nested swap notional/reset schedules and versioned
call/put entries keep their separate contracts.

PostgreSQL refuses invalid schedules before projection mutation. A failed new write or amendment
does not publish partial children, delete the prior projection, advance its version or move a replay
checkpoint. Migration `058_security_master_schedule_decimal_precision.sql` widens only the factor
and principal child-value columns to unconstrained PostgreSQL `numeric`, so every accepted CLR
`decimal` survives persistence without scale rounding or numeric overflow. Apply it through the
normal migration runner before using the corrected projection writer. It preserves existing stored
values; it does not reconstruct previously rounded projections or change retained event evidence.
Any rebuild from retained source terms remains an explicit operational action. Existing invalid
retained input requires an explicit governed correction before these consumers can use it.

The shared fixture corpus is under `tests/fixtures/security-master/schedule-validation/`.
`SecurityEconomicScheduleValidationTests` compares typed values or code/path diagnostics across
the resolver, schema and relational plan; `SecurityEconomicSchedulePostgresTests` exercises database
round trips and rollback with the same fixtures. Hosted checks and independent Security Master and
cash-flow review remain required before merge. Passing these tests does not establish that accounting
entries were previously incorrect or that operators have accepted a deployment.

## Asset-class operational wiring

The domain `SecurityKind` union, C# asset catalog, schema, codec, validators, relational projections,
lot conventions and pack membership are held in parity by `SecurityAssetClassRegistryCoverageTests`,
`SecurityAssetTermsSchemaRoundTripTests` and `SecurityAssetClassParityGuardTests`.
`SecurityAssetClassOperationalWiringTests` extends that guard through actual readiness rows and aliases.

Readiness definitions, depth targets, profile identities and classification keywords are owned by
`SecurityAssetPackRegistry`. Every catalog class receives a row. A new class without a specialized
operational profile stays `ReviewRequired`, including after provider evidence is retained; adding a
class never silently removes it from readiness. Broad operational vocabulary is scoped to readiness,
while canonical aliases are shared through the asset catalog. MBS, ABS, CLO and CMBS evidence belongs
only to StructuredCredit. Profile metadata, including nested fields, cannot contradict the registered
profile's canonical class. Custom profile documents still require explicit typed-term integration to
produce contractual schedules; a readiness row alone does not supply that integration.
