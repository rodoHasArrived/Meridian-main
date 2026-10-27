# Credit Support

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-25

## Definition

Credit Support is the collateral two parties to a master agreement exchange against the exposure
of the bilateral contracts between them. Meridian records it as three durable concepts on the OTC
contract model:

- an **Exposure Snapshot**: the derived, append-only valuation of one netting set on one date, with
  the contracts valued, the netting basis, gross and net exposure, the collateral each side holds
  and the credit support movement each side's terms imply;
- a **Collateral Balance**: collateral the fund has posted to, or received from, the counterparty
  under a collateral agreement: one eligible-collateral line, its quantity and unit value, the
  valued amount, the haircut copied from the agreement and the date the valuation is good for;
- a **Margin Call**: one movement from a snapshot taken through its life (issued, agreed, disputed,
  settled, cancelled), whose amount is copied from the snapshot, never typed.

A snapshot is computed, then recorded: `OtcExposureRules.Compute` turns the netting set, the
valuations of its contracts and the balances under its collateral agreement into an
`OtcExposureSnapshotDto`, and the store appends it as the next version for that date with a
reproducible digest. Balances and calls are versioned in place. Every write appends to the
Security Master governance log on the `OtcCollateral` stream in the same transaction.

## Relationships

- Belongs to a Netting Set and, through it, a Master Agreement and an optional Collateral
  Agreement (credit support annex) on the OTC contract model; the thresholds, minimum transfer
  amount, independent amount, rounding and eligible collateral come from the agreement.
- Values each contract through `IOtcContractValuationSource`. The first source marks a contract
  from the golden-copy price of its [Security](security.md): a percent-of-par observation is the
  value per 100 of notional to the buyer, so the mark is `sign(direction) × notional × price / 100`.
  Other sources plug into the same seam; no asset-class name is compared anywhere.
- A Collateral Balance may name the Security posted; a Margin Call cites the snapshot it was
  issued from and the collateral agreement it is called under.
- Nothing here reaches the Ledger: a snapshot is a derived valuation record, not a ledger
  balance, and settlement does not move a balance. The operator records the resulting balance.
- Stays apart from the workstation collateral exposure read model, a producer-fed in-memory
  buffer keyed by counterparty spelling, which is unchanged.

## Business Rules

- **Sign and currency.** Every amount is in the collateral agreement currency; a contract in another
  currency is unvalued (`CurrencyMismatch`) because this increment does no FX. A mark is signed from
  the fund's side: positive means the counterparty owes the fund.
- **Which contracts enter.** Open contracts (not terminated, novated or matured) traded on or before
  the as-of date, including `Pending` ones. An open contract past its termination date is a blocker
  (`PastTermination`); nothing matures contracts automatically.
- **Netting.** ISDA 1992, ISDA 2002, GMRA and GMSLA masters net on close-out: each side's exposure
  is the signed sum of marks. `Other` masters do not net (fail closed): each side's exposure is the
  sum of the marks in its favour. The basis is recorded on the snapshot.
- **Credit support, each way.** With `X` the exposure of the party as Secured Party and the other
  party as Pledgor, `CreditSupportAmount = max(0, X + IA_pledgor − IA_securedParty −
  Threshold_pledgor)`. The agreement's single independent amount is read as the amount the fund
  posts. `Held` is the haircut-adjusted value (`value × (1 − haircut / 100)`) of the balances the
  Secured Party holds: balances received, for the fund; balances posted, for the counterparty.
- **Minimum transfer, then rounding.** A delivery (`CreditSupportAmount − Held`) or return
  (`Held − CreditSupportAmount`) is tested against the minimum transfer amount unrounded; a delivery
  then rounds up and a return rounds down to the rounding amount, and a return that rounds to zero
  is no movement. The Pledgor delivers; the Secured Party returns. A snapshot carries up to two
  movements, one per side.
- **Fail closed.** Any unvalued contract, a missing, not-yet-effective or terminated collateral
  agreement or an open contract past termination leaves the snapshot `Incomplete`: exposures are reported for what was
  valued, no movement is computed and every reason is listed. A snapshot is never more real than
  its least-real input; its provenance is reported, and a call is issued only from a `Complete`,
  real snapshot.
- **Balances.** A balance is posted as one of the agreement's eligible-collateral lines, matched by
  description regardless of case; the haircut is copied from that line and the valued amount is
  quantity × unit value, never supplied. Cash is valued at one unit per unit and names no
  security. A quantity is positive when recorded and may be zero when amended (fully returned).
  The currency must equal the agreement's.
- **Calls.** A call copies the amount and currency of the snapshot's movement for its kind and
  mover, is issued on or after the snapshot date, falls due on or after it is issued, and is
  refused while the netting set has another open call of the same kind for the same mover (a
  party's delivery and its return are distinct obligations and may both be open) or when the
  snapshot has been superseded by a later version for its date. Issued → Agreed | Disputed | Cancelled;
  Disputed → Agreed | Cancelled; Agreed → Settled | Cancelled; Settled and Cancelled are terminal.
  Agreement defaults to the called amount, settlement to the agreed amount; a dispute or a
  cancellation states its reason; a settlement states its date.
- **Evidence.** Every snapshot names its rule version (`otc-exposure-rules-v1`), each contract's
  valuation method and inputs (the golden-copy selection receipt for the observed-price mark) and a
  SHA-256 digest of its payload; the governance log names the actor on every write.

## Examples

Fixture: an ISDA 2002 master; a USD CSA with thresholds 0 (fund) and 250,000 (counterparty),
minimum transfer 100,000, independent amount 1,000,000 and rounding 10,000; a 10,000,000 bought
swap and a 4,000,000 sold one; the fund has posted 400,000 cash (no haircut) and 2,000 Treasuries
at 100.125 with a 2% haircut (200,250 → 196,245), so the counterparty holds 596,245.

- Both contracts at 1.25% of notional: marks +125,000 and −50,000, net exposure 75,000. The fund
  as Secured Party needs `max(0, 75,000 − 1,000,000 − 250,000) = 0`. The counterparty as Secured
  Party needs `max(0, −75,000 + 1,000,000) = 925,000`; it holds 596,245, so the raw delivery is
  328,755, above the minimum transfer, rounding up to **the fund delivers 330,000**.
- Same marks, 1,600,000 cash posted: the counterparty holds 1,796,245 against 925,000, a raw return
  of 871,245 rounding down to **the counterparty returns 870,000**.
- Same marks, 750,000 cash posted: the raw return of 21,245 is below the minimum transfer; **no
  movement**.
- Same marks under an `Other` master: gross exposures 125,000 and 50,000; the counterparty needs
  1,050,000 and **the fund delivers 460,000**.
- Both contracts at 25%: net exposure 1,500,000. The fund needs 250,000 and holds nothing, so
  **the counterparty delivers 250,000**; the counterparty needs 0 and holds 596,245, so **the
  counterparty returns 590,000**. Two movements, one per side.
- The sold contract has no price: the snapshot is `Incomplete`, reports 125,000 of exposure, lists
  the `MissingInput` blocker and computes no movement.

## Future Expansion Notes

- Initial margin models (SIMM, schedule IM), segregated IM, regulatory IM flags and a directed or
  per-contract independent amount.
- FX between contract, collateral and agreement currencies; forward points and NDF fixings.
- Valuation beyond an observed percent-of-par price (discounting, curves, credit curves, vols)
  through further `IOtcContractValuationSource` implementations and a per-class valuation
  descriptor fact.
- Dispute amounts and tolerances, interest on late or cash collateral, substitution,
  rehypothecation, concentration limits, pricing of security collateral and staleness of
  collateral valuations.
- Automatic maturing of contracts past termination, a netting-opinion flag on the master,
  maker-checker on calls, a dedicated collateral permission, a browser or WPF screen and the
  retirement of the workstation collateral read model.

## Implementation and regression references

- [Exposure seam, snapshot and rules](../../src/Meridian.Contracts/SecurityMaster/OtcExposure.cs)
- [Balances, calls, store interface and rules](../../src/Meridian.Contracts/SecurityMaster/OtcCollateral.cs)
- [Observed-price valuation source](../../src/Meridian.Application/SecurityMaster/Otc/PricingStoreOtcContractValuationSource.cs)
  and [exposure service](../../src/Meridian.Application/SecurityMaster/Otc/OtcExposureService.cs)
- [PostgreSQL store](../../src/Meridian.Storage/SecurityMaster/PostgresOtcCollateralStore.cs) over
  migrations `054_otc_exposure_and_collateral.sql` and
  `055_security_governance_events_otc_collateral_stream.sql`
- [Endpoints](../../src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.OtcCollateral.cs)
- Regression scope: `OtcExposureRulesTests`, `OtcCollateralRulesTests`,
  `OtcCollateralMigrationTests`, `PricingStoreOtcContractValuationSourceTests`,
  `OtcCollateralPostgresTests` (PostgreSQL) and `SecurityMasterOtcCollateralEndpointsTests`.

These references identify the implementation and its regression scope. The pull request carries
actual validation results.
