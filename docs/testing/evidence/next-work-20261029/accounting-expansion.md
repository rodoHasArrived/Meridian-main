# Expanded LOT accounting treatments

The owner requested short opening/cover, factor-backed amortization and automatic yield-to-worst
on 2026-10-29. This note records the implemented boundary and required proof; it is not a
certification or an operator verdict.

| Treatment | Accounting rule | Authority and retained evidence |
| --- | --- | --- |
| Short opening | Debit cash, credit a liability for positive opening proceeds. | Explicit Short direction, matching Short/Liability position, canonical acquisition facts and existing independent posting approval. |
| Short cover | Debit relieved opening proceeds, credit execution cash cost, recognize proceeds less cost as book gain/loss. | Explicit functional-currency execution price, homogeneous canonical relief pool, effective relief-policy revision, versioned selections and existing approval. |
| Remaining-face amortization | Continue the acquisition schedule for surviving booked quantity times current/booked factor. Retain acquisition FX. | Explicit calculation v3, immutable paydown receipt and prior disposal/basis history; locked lot and Security Master inputs. |
| Automatic yield-to-worst | Solve the regular-coupon yield for maturity and each fixed-price European whole call/put. Select the minimum maturity/issuer-call yield, with deterministic date/kind tie-breaking. | Full alternative list and selected date/price/yield are retained in the reviewed v3 instruction and recomputed before posting. |

Holder puts are calculated and visible in the comparison, but a holder-controlled exercise cannot
force adverse issuer redemption. Yields are nominal annual rates compounded at coupon frequency,
consistent with the existing canonical constant-yield kernel. The comparison uses original
acquisition price and contractual terms; it does not substitute current marks or current FX.
For non-par redemption, the kernel re-denominates principal to the redemption amount and scales
coupon inversely, preserving the actual acquisition price and contractual cash coupon.

Example: 110 cost, annual coupon 10, two-year maturity and a one-year call at 100 gives a call
yield of zero. The first half-year basis is 105, and the call-date basis is 100. A put at 90 or
120 is priced separately and cannot displace that issuer-call selection. A zero-coupon purchase
at 90 with the same dates selects the lower two-year maturity yield rather than the one-year
call yield.

Example: 950,000 booked face at factor .95 and price 101.5 has cost 964,250. A paydown to .90
leaves basis 913,500 while durable booked quantity stays 9,500 units. At 75% of the two-year
straight-line life, remaining-face basis is 903,375: the amortization credit is 10,125. Applying
the factor to quantity a second time would understate holdings; omitting it from basis would
overstate amortization's target. The database golden case must reconcile both representations.

Historical v1/v2 instructions keep their original JSON, arithmetic and admission rules. Optional
v3 fields are omitted when absent. Exact replay still precedes current reference and period
admission. Corrections use immutable reversal/rebook receipts, never changes to posted journals.

Unsupported exercise windows, Bermudan/American exercise, make-whole formulas, notice/cadence
rules, irregular coupon dates, declared schedule conventions, structured principal or coupon
schedules and forecast prepayments still block. These need their own valuation inputs and proof;
they are not interpreted as absent. Short manufactured income, corporate actions and tax
character and short reversal/rebook also remain separate treatments. Tax reports refuse to infer short-sale character from
opening-lot age. Broader consumer certification and live Windows acceptance remain open.

Required proof includes balanced gain/flat/loss covers, positive remaining quantity and basis,
wrong-direction/account refusal, missing-price refusal, stale selections and changed replay,
paydown-to-amortization receipt reconstruction, factor and partial-disposal scaling, FX parity,
redemption selection/price tampering, historical serialization, reversal/rebook and period locks.
Run the full local gate and required hosted checks on the final candidate; record actual results
separately from this specification.
