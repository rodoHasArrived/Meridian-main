// Meridian position fixture — ONE instrument, TWO holdings of it, on two accounting bases.
// The security record is canonical and identical for both holdings (it is a property of the
// instrument). Everything under `positions[]` is a property of ONE holding and changes with the
// portfolio, entity, book, basis and acquisition history — that split is the whole point of the
// position object (guidelines/ANALYTICAL_SCOPE.md).
//
// Loaded by templates/position-detail/PositionDetail.dc.html and — for the Positions tab and the
// position drawer — by templates/security-master-registry/SecurityDetail.dc.html, which degrades
// to an empty state when this file is absent (copy either folder alone and nothing breaks).
(function (global) {
  "use strict";

  // ── The instrument ──────────────────────────────────────────────────────────
  // Non-agency RMBS, seasoned re-performing collateral. Fictional deal.
  var SECURITY = {
    id: "cmlt-2021r3-a2",
    name: "Cascade Mortgage Loan Trust 2021-R3 Class A2",
    shortCode: "CMLT 2021-R3 A2",
    cusip: "14789RAB6", isin: "US14789RAB63",
    issuer: "Cascade Mortgage Loan Trust 2021-R3",
    sponsor: "Cascade Residential Capital LLC",
    servicer: "Northline Loan Servicing",
    trustee: "Harbor Institutional Trust",
    assetClass: "Non-Agency RMBS", sector: "Structured product", currency: "USD",
    coupon: "3.750% fixed", couponFrequency: "Monthly, 25th", dayCount: "30/360",
    dated: "2021-08-25", firstPay: "2021-09-25", statedMaturity: "2061-08-25",
    originalDealSize: "$684,200,000", originalClassSize: "$212,000,000",
    classFactor: "0.6412", currentClassBalance: "$135,934,400",
    price: 97.312, priceLabel: "97-10", priceAsOf: "Jun 30, 2026 16:05 ET",
    priceSource: "Anchorage Pricing Service · evaluated bid",
    yieldToMaturity: "4.41%", yieldToWorst: "4.38%", oas: "168 bp", zSpread: "174 bp",
    wal: "5.2 yrs", duration: 3.84, spreadDuration: 4.21, convexity: -1.42, dv01PerMM: 384,
    rating: "AA (KBRA) · A+ (Fitch)", naicModel: "NAIC 2026 RMBS model · vintage 2026-05",
    // Structure — how the waterfall behaves
    structure: [
      { label: "Class", value: "A2 · senior sequential" },
      { label: "Payment priority", value: "Sequential pay, shifting interest" },
      { label: "Subordination below", value: "12.40%" },
      { label: "Overcollateralization", value: "0.55%" },
      { label: "Excess spread (annualized)", value: "1.18%" },
      { label: "Credit enhancement (current)", value: "12.95%" },
      { label: "Stepdown date (earliest)", value: "Aug 25, 2027" },
      { label: "Clean-up call", value: "10% of original balance" },
    ],
    // Collateral — what actually pays
    collateral: [
      { label: "Loan count", value: "1,428" },
      { label: "Pool type", value: "Re-performing, 24mo+ clean pay" },
      { label: "Weighted avg coupon", value: "4.62%" },
      { label: "Weighted avg loan age", value: "187 months" },
      { label: "Weighted avg LTV", value: "68.4%" },
      { label: "Weighted avg FICO", value: "702" },
      { label: "Modified loans", value: "61.7% of balance" },
      { label: "Top states", value: "CA 21.3% · FL 11.8% · NY 9.4%" },
    ],
    // Behavioural assumptions — the model inputs every projection depends on
    behaviour: [
      { label: "CPR (3-month)", value: "6.8" },
      { label: "CPR (12-month)", value: "7.4" },
      { label: "CDR (3-month)", value: "0.42" },
      { label: "60+ delinquency", value: "4.10% of current balance" },
      { label: "Loss severity assumption", value: "38%" },
      { label: "Cumulative loss to date", value: "0.38%" },
      { label: "Servicer advances", value: "Current · no stop-advance event" },
      { label: "Prepay model", value: "Meridian RMBS prepay v4.2 · CPR 6.8 base" },
    ],
    // Tests and dates that change the cash flows if they trip
    schedule: [
      { event: "Stepdown date", date: "Aug 25, 2027", test: "Credit enhancement ≥ 24.80%", current: "12.95%", status: "Not met", tone: "pending" },
      { event: "Cumulative loss trigger", date: "Each period", test: "≤ 1.25% of original balance", current: "0.38%", status: "Passing", tone: "confirmed" },
      { event: "60+ delinquency trigger", date: "Each period", test: "≤ 6.00% of current balance", current: "4.10%", status: "Passing", tone: "confirmed" },
      { event: "Clean-up call", date: "Aug 2029 (projected)", test: "Class balance ≤ 10% of original", current: "64.12%", status: "Not exercisable", tone: "na" },
      { event: "Servicer termination event", date: "—", test: "Servicer rating / performance", current: "None", status: "None", tone: "confirmed" },
    ],
    // Instrument risk — true wherever the thing is held
    instrumentRisk: [
      { metric: "Effective duration", value: "3.84 yrs", note: "Option-adjusted, CPR 6.8" },
      { metric: "Spread duration", value: "4.21 yrs", note: "To the OAS curve" },
      { metric: "DV01 per $1mm face", value: "$384", note: "Parallel shift" },
      { metric: "OAS", value: "168 bp", note: "To the swap curve" },
      { metric: "Convexity", value: "−1.42", note: "Negative — prepay optionality" },
      { metric: "Weighted average life", value: "5.2 yrs", note: "At base prepay" },
    ],
  };

  // ── The holdings ────────────────────────────────────────────────────────────
  var POSITIONS = [
    {
      id: "ga-fi-07", code: "GA-FI-07", securityId: SECURITY.id,
      portfolio: "General Account", portfolioId: "general-account",
      entity: "Meridian Life Insurance Co.", entityId: "meridian-life",
      book: "Statutory Investment Book", account: "GA-FI-01", accountName: "General Account — Fixed Income",
      strategy: "Core Fixed Income", manager: "Structured Products Desk", custodian: "Harbor Institutional Trust",
      basis: "Statutory", basisRef: "SSAP 43R", basisNote: "Amortized cost · effective yield, prospective",
      measurement: "Amortized cost", classification: "Loan-backed and structured securities",
      naic: "1.C", naicNote: "Modeled · NAIC 2026 RMBS model", rbcFactor: "0.97%", rbcCharge: 167646,
      condition: "confirmed", asOf: "Jun 30, 2026", openedOn: "Mar 14, 2022", lots: 3,
      quantity: 18000000, quantityLabel: "18,000,000", quantityUnit: "current face",
      originalFace: 20600000,
      price: 97.312, marketValue: 17516160, bookValue: 17283072, costBasis: 16838740,
      accretedDiscount: 444332, originalCost: 19271000, unrealized: 233088, accrued: 9375,
      weight: "0.82%", weightNote: "of $2.146B portfolio market value",
      durationContribution: "+0.031 yrs", spreadContribution: "+0.034 yrs", dv01: 6726,
      oasContribution: "1.4 bp", convexityContribution: "−0.012",
      coverage: { attached: 6, required: 6, label: "evidence items" },
      // §9 Overview — value · risk · accounting · activity
      overview: {
        value: [
          { label: "Market value", value: "$17,516,160", size: "lg" },
          { label: "Book value (BACV)", value: "$17,283,072", size: "lg" },
          { label: "Unrealized gain", value: "+$233,088", note: "Disclosure only — not carried at statutory" },
          { label: "Portfolio weight", value: "0.82%", scope: "portfolio" },
        ],
        risk: [
          { label: "Effective duration", value: "3.84 yrs", scope: "security", explain: true },
          { label: "Position DV01", value: "$6,726" },
          { label: "Duration contribution", value: "+0.031 yrs", scope: "portfolio" },
          { label: "Spread duration", value: "4.21 yrs", scope: "security" },
        ],
        accounting: [
          { label: "Basis", value: "Statutory · SSAP 43R" },
          { label: "Measurement", value: "Amortized cost" },
          { label: "NAIC designation", value: "1.C" },
          { label: "Accrued interest", value: "$9,375" },
        ],
        activity: [
          { label: "Last trade", value: "Nov 21, 2024", note: "Buy 5,000,000 face @ 95.250" },
          { label: "Principal returned YTD", value: "$640,000", note: "6 monthly paydowns" },
          { label: "Investment income YTD", value: "$411,350" },
          { label: "Open tax lots", value: "3", scope: "lot" },
        ],
      },
      // Overview's dominant analytical view — market value, period movement proved
      mvRoll: {
        openingLabel: "Market value · Jan 1, 2026", opening: 17679600,
        closingLabel: "Market value · Jun 30, 2026", closing: 17516160,
        movements: [
          { label: "Principal returned at par", value: -640000, note: "6 paydowns · 640,000 face", linkLabel: "Transactions" },
          { label: "Price change", value: 476560, note: "94.875 → 97.312", linkLabel: "Evidence" },
        ],
      },
      // §10 Accounting
      accounting: {
        kv: [
          { label: "Accounting basis", value: "Statutory (SSAP 43R)" },
          { label: "NAIC designation", value: "1.C" },
          { label: "Asset classification", value: "Loan-backed and structured securities" },
          { label: "Measurement basis", value: "Amortized cost" },
          { label: "Accretion method", value: "Effective yield, prospective" },
          { label: "Original cost (3 lots)", value: "$19,271,000" },
          { label: "Relieved cost basis", value: "$16,838,740" },
          { label: "Accreted discount to date", value: "$444,332" },
          { label: "Adjusted (amortized) cost", value: "$17,283,072" },
          { label: "Fair value", value: "$17,516,160" },
          { label: "Unrealized gain (disclosure)", value: "$233,088" },
          { label: "Accrued interest", value: "$9,375" },
          { label: "Impairment recognized", value: "None" },
          { label: "RBC factor · charge", value: "0.97% · $167,646" },
          { label: "Admitted asset", value: "Fully admitted" },
          { label: "Book close", value: "Q2 2026 · soft-closed" },
        ],
        // §10 cost basis trace — the Trace primitive, arithmetic on screen
        trace: {
          title: "Adjusted cost basis · GA-FI-07",
          formula: "Relieved acquisition cost + accreted discount − amortized premium − impairment = adjusted cost basis",
          technicalFormula: "amortized_cost = Σ_lots(remaining_face × lot_price) + accretion(effective_yield, prospective) − otti",
          steps: [
            { operator: "", label: "Acquisition cost, 3 lots, relieved to current face", value: "$16,838,740", source: "Tax lot ledger", sourceId: "LOT-GA-FI-07", asOf: "Jun 30, 2026", condition: "confirmed", detail: "18,000,000 face remaining of 20,600,000 acquired" },
            { operator: "+", label: "Accreted discount to date", value: "$444,332", source: "Investment accounting engine", sourceId: "ACC-2026-06-LBSS", asOf: "Jun 30, 2026", condition: "confirmed", detail: "Effective yield 4.62%, prospective re-estimate each period" },
            { operator: "−", label: "Amortized premium", value: "$0", detail: "Acquired at a discount on every lot", condition: "na" },
            { operator: "−", label: "Other-than-temporary impairment", value: "$0", source: "Impairment review Q2 2026", sourceId: "IMP-2026-Q2-0431", condition: "confirmed", detail: "Cash flow test passed — no adverse change in expected cash flows" },
          ],
          result: { label: "Adjusted cost basis (BACV)", value: "$17,283,072" },
          rounding: "Half-even · 2 decimal places",
          asOf: "Jun 30, 2026", basis: "Statutory", condition: "confirmed",
          calculationVersion: "ACCT 5.2.1", calculatedAt: "Jul 1, 2026 02:40 UTC",
          executedBy: "Investment accounting engine", engine: "meridian-accounting@5.2.1",
          reproducibility: "Reproducing this basis requires the same lot relief order (FIFO), the same prospective yield re-estimate, and the same prepayment vector. A faster prepay vector raises accretion and moves the basis.",
        },
        // Period rollforward of the carried value
        roll: {
          openingLabel: "Adjusted cost · Jan 1, 2026", opening: 17856000,
          closingLabel: "Adjusted cost · Jun 30, 2026", closing: 17283072,
          movements: [
            { label: "Cost relieved on principal paydowns", value: -614464, note: "640,000 face at 96.01", linkLabel: "Lot ledger" },
            { label: "Discount accretion", value: 41536, note: "Effective yield · 6 periods", linkLabel: "Journal" },
            { label: "Impairment", value: 0, note: "None recognized" },
          ],
        },
        journal: [
          { date: "Jun 25, 2026", entry: "JE-26-06-4412", description: "Interest income received", debit: "Cash $56,557", credit: "Interest income $56,557", status: "Posted" },
          { date: "Jun 25, 2026", entry: "JE-26-06-4413", description: "Principal paydown", debit: "Cash $98,300", credit: "Investment $94,349 · Realized gain $3,951", status: "Posted" },
          { date: "Jun 30, 2026", entry: "JE-26-06-4530", description: "Discount accretion", debit: "Investment in LBSS $6,780", credit: "Interest income $6,780", status: "Posted" },
          { date: "Jun 30, 2026", entry: "JE-26-06-4602", description: "Fair value disclosure — memo only", debit: "—", credit: "—", status: "Posted" },
          { date: "Jul 1, 2026", entry: "JE-26-07-0004", description: "NAIC designation refresh — 2026 model vintage", debit: "—", credit: "—", status: "Draft" },
        ],
        recon: [
          { line: "Current face", ledger: "18,000,000", custodian: "18,000,000", diff: "—", status: "Agreed" },
          { line: "Market value", ledger: "$17,516,160", custodian: "$17,516,160", diff: "—", status: "Agreed" },
          { line: "Accrued interest", ledger: "$9,375", custodian: "$9,375", diff: "—", status: "Agreed" },
          { line: "Principal received YTD", ledger: "$640,000", custodian: "$640,000", diff: "—", status: "Agreed" },
        ],
        reconNote: "Reconciled to the custodian position file of Jun 30, 2026 (HRB-POS-20260630) with no explaining items.",
      },
      // §11 Risk — instrument metric against position contribution
      risk: {
        rows: [
          { metric: "Effective duration", instrument: "3.84 yrs", contributionLabel: "Portfolio duration contribution", contribution: "+0.031 yrs" },
          { metric: "Spread duration", instrument: "4.21 yrs", contributionLabel: "Spread risk contribution", contribution: "+0.034 yrs" },
          { metric: "DV01", instrument: "$384 per $1mm", contributionLabel: "Position DV01", contribution: "$6,726" },
          { metric: "OAS", instrument: "168 bp", contributionLabel: "Contribution to portfolio OAS", contribution: "1.4 bp" },
          { metric: "Convexity", instrument: "−1.42", contributionLabel: "Convexity contribution", contribution: "−0.012" },
          { metric: "Weighted average life", instrument: "5.2 yrs", contributionLabel: "Share of portfolio WAL", contribution: "0.04 yrs" },
        ],
        scenarios: [
          { label: "+100 bp parallel", impact: "−$689,000", pct: "−3.93%", dir: "down", note: "Negative convexity widens the loss" },
          { label: "−100 bp parallel", impact: "+$631,000", pct: "+3.60%", dir: "up", note: "Prepay acceleration caps the gain" },
          { label: "+50 bp spread", impact: "−$369,000", pct: "−2.11%", dir: "down", note: "Spread duration 4.21" },
          { label: "CPR 6.8 → 12.0", impact: "−$142,000", pct: "−0.81%", dir: "down", note: "Faster return of a discount basis" },
          { label: "60+ DQ +200 bp", impact: "−$214,000", pct: "−1.22%", dir: "down", note: "Approaches the 6.00% trigger" },
          { label: "Home prices −10%", impact: "−$486,000", pct: "−2.77%", dir: "down", note: "Severity 38% → 46%" },
        ],
        note: "Contributions are measured against the General Account portfolio of $2.146B market value, as of Jun 30, 2026, risk run RA-2026-06-30.",
      },
      // §13 Transactions — lifecycle, then the ledger
      transactions: {
        qtyRoll: {
          openingLabel: "Opening face · Jan 1, 2026", opening: 18640000,
          closingLabel: "Closing face · Jun 30, 2026", closing: 18000000,
          movements: [
            { label: "Purchases", value: 0, note: "None this period" },
            { label: "Principal paydowns", value: -640000, note: "6 monthly distributions" },
            { label: "Sales", value: 0, note: "None this period" },
            { label: "Transfers", value: 0, note: "None this period" },
          ],
        },
        lifecycle: [
          { date: "Mar 14, 2022", type: "Buy", detail: "8,000,000 face @ 93.125", amount: "$7,450,000" },
          { date: "Jun 8, 2023", type: "Buy", detail: "7,600,000 face @ 92.875", amount: "$7,058,500" },
          { date: "Nov 21, 2024", type: "Buy", detail: "5,000,000 face @ 95.250", amount: "$4,762,500" },
          { date: "Jan–May 2026", type: "Paydown", detail: "541,700 face returned at par", amount: "$541,700" },
          { date: "Jun 25, 2026", type: "Paydown", detail: "98,300 face returned at par", amount: "$98,300" },
        ],
        lots: [
          { lot: "L-2203-001", acquired: "Mar 14, 2022", originalFace: "8,000,000", remainingFace: "6,990,000", price: "93.125", remainingCost: "$6,509,438", gain: "+$100,463" },
          { lot: "L-2306-004", acquired: "Jun 8, 2023", originalFace: "7,600,000", remainingFace: "6,641,000", price: "92.875", remainingCost: "$6,167,829", gain: "+$95,486" },
          { lot: "L-2411-002", acquired: "Nov 21, 2024", originalFace: "5,000,000", remainingFace: "4,369,000", price: "95.250", remainingCost: "$4,161,473", gain: "+$90,033" },
        ],
        ledger: [
          { date: "Jun 25, 2026", type: "Paydown", face: "98,300", price: "100.000", amount: "$98,300", basis: "$94,349", realized: "+$3,951", settled: "Jun 25, 2026" },
          { date: "May 25, 2026", type: "Paydown", face: "99,300", price: "100.000", amount: "$99,300", basis: "$95,308", realized: "+$3,992", settled: "May 25, 2026" },
          { date: "Apr 25, 2026", type: "Paydown", face: "104,900", price: "100.000", amount: "$104,900", basis: "$100,681", realized: "+$4,219", settled: "Apr 27, 2026" },
          { date: "Mar 25, 2026", type: "Paydown", face: "106,400", price: "100.000", amount: "$106,400", basis: "$102,121", realized: "+$4,279", settled: "Mar 25, 2026" },
          { date: "Nov 21, 2024", type: "Buy", face: "5,000,000", price: "95.250", amount: "$4,762,500", basis: "$4,762,500", realized: "—", settled: "Nov 25, 2024" },
          { date: "Jun 8, 2023", type: "Buy", face: "7,600,000", price: "92.875", amount: "$7,058,500", basis: "$7,058,500", realized: "—", settled: "Jun 12, 2023" },
          { date: "Mar 14, 2022", type: "Buy", face: "8,000,000", price: "93.125", amount: "$7,450,000", basis: "$7,450,000", realized: "—", settled: "Mar 16, 2022" },
        ],
      },
      // §14 Income
      income: {
        kv: [
          { label: "Coupon income YTD", value: "$344,278" },
          { label: "Discount accretion YTD", value: "$41,536" },
          { label: "Premium amortization YTD", value: "$0" },
          { label: "Realized gain on paydowns YTD", value: "$25,536" },
          { label: "Total investment income YTD", value: "$411,350" },
          { label: "Coupon income MTD", value: "$56,557" },
          { label: "Accrued interest", value: "$9,375" },
          { label: "Effective yield", value: "4.62%" },
        ],
        received: [
          { period: "Jun 2026", face: "18,098,300", coupon: "$56,557", principal: "$98,300", total: "$154,857", condition: "confirmed" },
          { period: "May 2026", face: "18,197,600", coupon: "$56,868", principal: "$99,300", total: "$156,168", condition: "confirmed" },
          { period: "Apr 2026", face: "18,302,500", coupon: "$57,195", principal: "$104,900", total: "$162,095", condition: "confirmed" },
          { period: "Mar 2026", face: "18,408,900", coupon: "$57,528", principal: "$106,400", total: "$163,928", condition: "confirmed" },
          { period: "Feb 2026", face: "18,521,600", coupon: "$57,880", principal: "$112,700", total: "$170,580", condition: "confirmed" },
          { period: "Jan 2026", face: "18,640,000", coupon: "$58,250", principal: "$118,400", total: "$176,650", condition: "confirmed" },
        ],
        projected: [
          { period: "Q3 2026", principal: "$318,000", interest: "$167,200", total: "$485,200", condition: "estimated" },
          { period: "Q4 2026", principal: "$312,000", interest: "$164,200", total: "$476,200", condition: "estimated" },
          { period: "Q1 2027", principal: "$306,000", interest: "$161,300", total: "$467,300", condition: "estimated" },
          { period: "Q2 2027", principal: "$301,000", interest: "$158,400", total: "$459,400", condition: "estimated" },
          { period: "Q3 2027", principal: "$295,000", interest: "$155,600", total: "$450,600", condition: "estimated" },
          { period: "Q4 2027", principal: "$290,000", interest: "$152,900", total: "$442,900", condition: "estimated" },
          { period: "2028", principal: "$1,120,000", interest: "$583,000", total: "$1,703,000", condition: "estimated" },
          { period: "2029 and beyond", principal: "$15,058,000", interest: "$4,820,000", total: "$19,878,000", condition: "estimated" },
        ],
        projectedNote: "Projected at base CPR 6.8 with the stepdown test failing through Nov 2028. A faster vector pulls principal forward and shortens the interest stream — the projection is an assumption, not a schedule.",
      },
      // Compliance — clean position
      compliance: {
        status: "Ready",
        kv: [
          { label: "Restricted list", value: "Not listed" },
          { label: "Statutory admissibility", value: "Fully admitted" },
          { label: "Investment policy", value: "Within mandate" },
          { label: "Last review", value: "Jun 30, 2026 · Investment Compliance" },
        ],
        checks: [
          { code: "IPS-STRUCT-15", severity: "Ready", message: "Structured product ≤ 15% of admitted assets — currently 11.4%" },
          { code: "IPS-DEAL-100", severity: "Ready", message: "Single-deal concentration ≤ 1.00% of admitted assets — currently 0.81%" },
          { code: "IPS-NAIC-3", severity: "Ready", message: "NAIC 3 or below ≤ 5% of the portfolio — this holding is 1.C" },
          { code: "SSAP-43R-01", severity: "Ready", message: "Loan-backed classification and amortized-cost measurement agree with the NAIC designation" },
          { code: "RBC-CHK-02", severity: "Ready", message: "RBC charge $167,646 booked at the 0.97% factor for NAIC 1.C" },
        ],
      },
      // §15 Evidence — the Trace primitive, not an audit table
      evidence: {
        trace: {
          title: "Market value · GA-FI-07",
          formula: "Evaluated price × FX rate × current face = market value",
          technicalFormula: "mv = price/100 × fx(USD→USD) × current_face",
          steps: [
            { operator: "", label: "Evaluated price", value: "97.312", unit: "per 100", source: "Anchorage Pricing Service", sourceId: "APS-14789RAB6-20260630", asOf: "Jun 30, 2026 16:05 ET", condition: "confirmed", detail: "Evaluated bid, three-dealer corroboration" },
            { operator: "×", label: "FX rate", value: "1.0000", source: "Reporting currency is USD", condition: "na", detail: "No conversion applied" },
            { operator: "×", label: "Current face", value: "18,000,000", source: "Custodian holdings file", sourceId: "HRB-POS-20260630", asOf: "Jun 30, 2026", condition: "confirmed", detail: "Agrees to the ledger without explaining items" },
          ],
          result: { label: "Market value", value: "$17,516,160" },
          rounding: "Half-even · 2 decimal places",
          asOf: "Jun 30, 2026", basis: "Statutory", condition: "confirmed",
          calculationVersion: "VAL 4.1.0", calculatedAt: "Jun 30, 2026 22:12 UTC",
          executedBy: "Valuation engine", engine: "meridian-valuation@4.1.0",
          reproducibility: "Reproducing this value requires the same evaluated price date and the same custodian face. A re-published price or an intra-month paydown changes it.",
        },
        sources: [
          { label: "Price", value: "Anchorage Pricing Service", mono: false },
          { label: "Quantity", value: "Harbor Institutional Trust", mono: false },
          { label: "Accrual", value: "Investment accounting engine", mono: false },
          { label: "NAIC model", value: "2026 vintage · 2026-05", mono: false },
          { label: "Data steward", value: "Priya Raman", mono: false },
        ],
        items: [
          { name: "Custodian position file — Jun 30, 2026", kind: "Statement", received: "Jul 1, 2026 04:10 UTC", condition: "confirmed", note: "HRB-POS-20260630" },
          { name: "Evaluated price file — Jun 30, 2026", kind: "Vendor feed", received: "Jun 30, 2026 21:40 UTC", condition: "confirmed", note: "APS daily evaluated bid" },
          { name: "Remittance report — Jun 2026", kind: "Trustee report", received: "Jun 27, 2026 13:05 UTC", condition: "confirmed", note: "Northline servicer remittance" },
          { name: "NAIC RMBS model output — 2026 vintage", kind: "Model output", received: "May 12, 2026 09:00 UTC", condition: "confirmed", note: "Designation 1.C" },
          { name: "Impairment review — Q2 2026", kind: "Working paper", received: "Jun 30, 2026 18:22 UTC", condition: "confirmed", note: "IMP-2026-Q2-0431" },
          { name: "Trade confirmations — 3 lots", kind: "Confirmation", received: "Nov 25, 2024 (latest)", condition: "confirmed", note: "Broker confirms on file" },
        ],
        overrides: "None. No figure on this position is operator-overridden.",
      },
    },
    {
      id: "sa-vul-02", code: "SA-VUL-02", securityId: SECURITY.id,
      portfolio: "Separate Account", portfolioId: "separate-account",
      entity: "Meridian Life Separate Account A", entityId: "meridian-sep-a",
      book: "Separate Account Book", account: "SA-VUL-02", accountName: "Separate Account — VUL Sub-account",
      strategy: "Structured Income Sleeve", manager: "Structured Products Desk", custodian: "Harbor Institutional Trust",
      basis: "GAAP", basisRef: "ASC 320 · trading", basisNote: "Fair value through earnings",
      measurement: "Fair value", classification: "Trading securities",
      naic: "n/a", naicNote: "Separate account — no general-account RBC charge", rbcFactor: "—", rbcCharge: 0,
      condition: "confirmed", asOf: "Jun 30, 2026", openedOn: "Feb 19, 2026", lots: 1,
      quantity: 6500000, quantityLabel: "6,500,000", quantityUnit: "current face",
      originalFace: 6730000,
      price: 97.312, marketValue: 6325280, bookValue: 6325280, costBasis: 6045000,
      accretedDiscount: 0, originalCost: 6258900, unrealized: 280280, accrued: 3385,
      weight: "1.54%", weightNote: "of $412.0M sub-account market value",
      durationContribution: "+0.059 yrs", spreadContribution: "+0.065 yrs", dv01: 2429,
      oasContribution: "2.6 bp", convexityContribution: "−0.022",
      coverage: { attached: 4, required: 4, label: "evidence items" },
      overview: {
        value: [
          { label: "Fair value", value: "$6,325,280", size: "lg" },
          { label: "Carrying value", value: "$6,325,280", size: "lg", note: "Fair value is the carried amount" },
          { label: "Unrealized gain", value: "+$280,280", note: "Through earnings — ASC 320 trading" },
          { label: "Sub-account weight", value: "1.54%", scope: "portfolio" },
        ],
        risk: [
          { label: "Effective duration", value: "3.84 yrs", scope: "security", explain: true },
          { label: "Position DV01", value: "$2,429" },
          { label: "Duration contribution", value: "+0.059 yrs", scope: "portfolio" },
          { label: "Spread duration", value: "4.21 yrs", scope: "security" },
        ],
        accounting: [
          { label: "Basis", value: "GAAP · ASC 320" },
          { label: "Measurement", value: "Fair value (trading)" },
          { label: "NAIC designation", value: "n/a" },
          { label: "Accrued interest", value: "$3,385" },
        ],
        activity: [
          { label: "Last trade", value: "Feb 19, 2026", note: "Buy 6,730,000 face @ 93.000" },
          { label: "Principal returned YTD", value: "$230,000", note: "4 monthly paydowns" },
          { label: "Investment income YTD", value: "$99,132" },
          { label: "Open tax lots", value: "1", scope: "lot" },
        ],
      },
      mvRoll: {
        openingLabel: "Cost at acquisition · Feb 19, 2026", opening: 6258900,
        closingLabel: "Fair value · Jun 30, 2026", closing: 6325280,
        movements: [
          { label: "Cost relieved on paydowns", value: -213900, note: "230,000 face at 93.00", linkLabel: "Transactions" },
          { label: "Fair value remeasurement", value: 280280, note: "93.000 → 97.312, through earnings", linkLabel: "Evidence" },
        ],
      },
      accounting: {
        kv: [
          { label: "Accounting basis", value: "GAAP (ASC 320)" },
          { label: "Classification", value: "Trading — separate account" },
          { label: "Measurement basis", value: "Fair value" },
          { label: "Fair value hierarchy", value: "Level 2" },
          { label: "NAIC designation", value: "n/a — separate account" },
          { label: "Acquisition cost", value: "$6,258,900" },
          { label: "Cost basis (relieved)", value: "$6,045,000" },
          { label: "Fair value", value: "$6,325,280" },
          { label: "Unrealized in earnings", value: "$280,280" },
          { label: "Accretion recognized", value: "$0 — fair value basis" },
          { label: "Accrued interest", value: "$3,385" },
          { label: "Impairment recognized", value: "n/a under fair value" },
          { label: "RBC charge", value: "None — separate account" },
          { label: "Book close", value: "Q2 2026 · soft-closed" },
        ],
        trace: {
          title: "Carrying value · SA-VUL-02",
          formula: "Cost basis + unrealized remeasurement = carrying value (fair value)",
          technicalFormula: "carrying = fair_value = price/100 × current_face  (ASC 320 trading; no amortized-cost layer)",
          steps: [
            { operator: "", label: "Cost basis, relieved to current face", value: "$6,045,000", source: "Tax lot ledger", sourceId: "LOT-SA-VUL-02", asOf: "Jun 30, 2026", condition: "confirmed", detail: "6,500,000 face remaining of 6,730,000 acquired at 93.00" },
            { operator: "+", label: "Unrealized remeasurement through earnings", value: "$280,280", source: "Valuation engine", sourceId: "VAL-20260630-SA", asOf: "Jun 30, 2026", condition: "confirmed", detail: "No amortized-cost layer exists on this basis" },
          ],
          result: { label: "Carrying value (fair value)", value: "$6,325,280" },
          rounding: "Half-even · 2 decimal places",
          asOf: "Jun 30, 2026", basis: "GAAP", condition: "confirmed",
          calculationVersion: "ACCT 5.2.1", calculatedAt: "Jul 1, 2026 02:40 UTC",
          executedBy: "Investment accounting engine", engine: "meridian-accounting@5.2.1",
          reproducibility: "The same evaluated price and the same custodian face reproduce this carrying value exactly — there is no accretion assumption in it. That is the difference from the statutory holding of the same instrument.",
        },
        roll: {
          openingLabel: "Carrying value · Feb 19, 2026", opening: 6258900,
          closingLabel: "Carrying value · Jun 30, 2026", closing: 6325280,
          movements: [
            { label: "Cost relieved on principal paydowns", value: -213900, note: "230,000 face at 93.00", linkLabel: "Lot ledger" },
            { label: "Fair value remeasurement", value: 280280, note: "Through earnings", linkLabel: "Journal" },
          ],
        },
        journal: [
          { date: "Jun 25, 2026", entry: "JE-26-06-7731", description: "Interest income received", debit: "Cash $20,488", credit: "Interest income $20,488", status: "Posted" },
          { date: "Jun 25, 2026", entry: "JE-26-06-7732", description: "Principal paydown", debit: "Cash $56,100", credit: "Investment $52,173 · Realized gain $3,927", status: "Posted" },
          { date: "Jun 30, 2026", entry: "JE-26-06-7810", description: "Fair value remeasurement — trading", debit: "Investment $61,940", credit: "Change in fair value $61,940", status: "Posted" },
          { date: "Jun 30, 2026", entry: "JE-26-06-7811", description: "Separate account liability offset", debit: "Change in fair value $61,940", credit: "Separate account liability $61,940", status: "Posted" },
        ],
        recon: [
          { line: "Current face", ledger: "6,500,000", custodian: "6,500,000", diff: "—", status: "Agreed" },
          { line: "Fair value", ledger: "$6,325,280", custodian: "$6,325,280", diff: "—", status: "Agreed" },
          { line: "Accrued interest", ledger: "$3,385", custodian: "$3,385", diff: "—", status: "Agreed" },
          { line: "Principal received YTD", ledger: "$230,000", custodian: "$230,000", diff: "—", status: "Agreed" },
        ],
        reconNote: "Reconciled to the same custodian file as the general-account holding; the two holdings differ in basis, not in quantity evidence.",
      },
      risk: {
        rows: [
          { metric: "Effective duration", instrument: "3.84 yrs", contributionLabel: "Sub-account duration contribution", contribution: "+0.059 yrs" },
          { metric: "Spread duration", instrument: "4.21 yrs", contributionLabel: "Spread risk contribution", contribution: "+0.065 yrs" },
          { metric: "DV01", instrument: "$384 per $1mm", contributionLabel: "Position DV01", contribution: "$2,429" },
          { metric: "OAS", instrument: "168 bp", contributionLabel: "Contribution to sub-account OAS", contribution: "2.6 bp" },
          { metric: "Convexity", instrument: "−1.42", contributionLabel: "Convexity contribution", contribution: "−0.022" },
          { metric: "Weighted average life", instrument: "5.2 yrs", contributionLabel: "Share of sub-account WAL", contribution: "0.08 yrs" },
        ],
        scenarios: [
          { label: "+100 bp parallel", impact: "−$249,000", pct: "−3.93%", dir: "down", note: "Same instrument behaviour, smaller holding" },
          { label: "−100 bp parallel", impact: "+$228,000", pct: "+3.60%", dir: "up", note: "Prepay acceleration caps the gain" },
          { label: "+50 bp spread", impact: "−$133,000", pct: "−2.11%", dir: "down", note: "Straight to earnings on this basis" },
          { label: "CPR 6.8 → 12.0", impact: "−$51,000", pct: "−0.81%", dir: "down", note: "Faster return of a discount basis" },
          { label: "60+ DQ +200 bp", impact: "−$77,000", pct: "−1.22%", dir: "down", note: "Approaches the 6.00% trigger" },
          { label: "Home prices −10%", impact: "−$176,000", pct: "−2.77%", dir: "down", note: "Severity 38% → 46%" },
        ],
        note: "Contributions are measured against the VUL sub-account of $412.0M market value. The instrument column is identical to the general-account holding — only the contribution column differs.",
      },
      transactions: {
        qtyRoll: {
          openingLabel: "Opening face · Feb 19, 2026", opening: 6730000,
          closingLabel: "Closing face · Jun 30, 2026", closing: 6500000,
          movements: [
            { label: "Purchases", value: 0, note: "Opening lot only" },
            { label: "Principal paydowns", value: -230000, note: "4 monthly distributions" },
            { label: "Sales", value: 0, note: "None this period" },
            { label: "Transfers", value: 0, note: "None this period" },
          ],
        },
        lifecycle: [
          { date: "Feb 19, 2026", type: "Buy", detail: "6,730,000 face @ 93.000", amount: "$6,258,900" },
          { date: "Mar 25, 2026", type: "Paydown", detail: "58,900 face returned at par", amount: "$58,900" },
          { date: "Apr 25, 2026", type: "Paydown", detail: "58,100 face returned at par", amount: "$58,100" },
          { date: "May 25, 2026", type: "Paydown", detail: "56,900 face returned at par", amount: "$56,900" },
          { date: "Jun 25, 2026", type: "Paydown", detail: "56,100 face returned at par", amount: "$56,100" },
        ],
        lots: [
          { lot: "L-2602-011", acquired: "Feb 19, 2026", originalFace: "6,730,000", remainingFace: "6,500,000", price: "93.000", remainingCost: "$6,045,000", gain: "+$280,280" },
        ],
        ledger: [
          { date: "Jun 25, 2026", type: "Paydown", face: "56,100", price: "100.000", amount: "$56,100", basis: "$52,173", realized: "+$3,927", settled: "Jun 25, 2026" },
          { date: "May 25, 2026", type: "Paydown", face: "56,900", price: "100.000", amount: "$56,900", basis: "$52,917", realized: "+$3,983", settled: "May 25, 2026" },
          { date: "Apr 25, 2026", type: "Paydown", face: "58,100", price: "100.000", amount: "$58,100", basis: "$54,033", realized: "+$4,067", settled: "Apr 27, 2026" },
          { date: "Mar 25, 2026", type: "Paydown", face: "58,900", price: "100.000", amount: "$58,900", basis: "$54,777", realized: "+$4,123", settled: "Mar 25, 2026" },
          { date: "Feb 19, 2026", type: "Buy", face: "6,730,000", price: "93.000", amount: "$6,258,900", basis: "$6,258,900", realized: "—", settled: "Feb 23, 2026" },
        ],
      },
      income: {
        kv: [
          { label: "Coupon income YTD", value: "$83,032" },
          { label: "Discount accretion YTD", value: "$0", note: "None on a fair-value basis" },
          { label: "Premium amortization YTD", value: "$0" },
          { label: "Realized gain on paydowns YTD", value: "$16,100" },
          { label: "Total investment income YTD", value: "$99,132" },
          { label: "Coupon income MTD", value: "$20,488" },
          { label: "Accrued interest", value: "$3,385" },
          { label: "Current yield on cost", value: "4.03%" },
        ],
        received: [
          { period: "Jun 2026", face: "6,556,100", coupon: "$20,488", principal: "$56,100", total: "$76,588", condition: "confirmed" },
          { period: "May 2026", face: "6,613,000", coupon: "$20,666", principal: "$56,900", total: "$77,566", condition: "confirmed" },
          { period: "Apr 2026", face: "6,671,100", coupon: "$20,847", principal: "$58,100", total: "$78,947", condition: "confirmed" },
          { period: "Mar 2026", face: "6,730,000", coupon: "$21,031", principal: "$58,900", total: "$79,931", condition: "confirmed" },
        ],
        projected: [
          { period: "Q3 2026", principal: "$115,000", interest: "$60,400", total: "$175,400", condition: "estimated" },
          { period: "Q4 2026", principal: "$113,000", interest: "$59,300", total: "$172,300", condition: "estimated" },
          { period: "Q1 2027", principal: "$110,000", interest: "$58,200", total: "$168,200", condition: "estimated" },
          { period: "Q2 2027", principal: "$109,000", interest: "$57,200", total: "$166,200", condition: "estimated" },
          { period: "Q3 2027", principal: "$107,000", interest: "$56,200", total: "$163,200", condition: "estimated" },
          { period: "Q4 2027", principal: "$105,000", interest: "$55,200", total: "$160,200", condition: "estimated" },
          { period: "2028", principal: "$404,000", interest: "$210,500", total: "$614,500", condition: "estimated" },
          { period: "2029 and beyond", principal: "$5,437,000", interest: "$1,740,000", total: "$7,177,000", condition: "estimated" },
        ],
        projectedNote: "Same prepay vector as the general-account holding — one instrument, one projection, scaled to this face. Divergent projections on one instrument would be a finding.",
      },
      compliance: {
        status: "Ready",
        kv: [
          { label: "Restricted list", value: "Not listed" },
          { label: "Statutory admissibility", value: "n/a — separate account" },
          { label: "Sub-account mandate", value: "Within mandate" },
          { label: "Last review", value: "Jun 30, 2026 · Investment Compliance" },
        ],
        checks: [
          { code: "SA-MANDATE-08", severity: "Ready", message: "Structured income sleeve ≤ 20% of sub-account net assets — currently 14.8%" },
          { code: "SA-DEAL-200", severity: "Ready", message: "Single-deal concentration ≤ 2.00% of net assets — currently 1.54%" },
          { code: "ASC-320-01", severity: "Ready", message: "Trading classification and fair-value measurement agree" },
          { code: "SA-UNIT-VAL", severity: "Ready", message: "Position included in the Jun 30 unit valuation at the same evaluated price" },
        ],
      },
      evidence: {
        trace: {
          title: "Fair value · SA-VUL-02",
          formula: "Evaluated price × FX rate × current face = fair value",
          technicalFormula: "fv = price/100 × fx(USD→USD) × current_face",
          steps: [
            { operator: "", label: "Evaluated price", value: "97.312", unit: "per 100", source: "Anchorage Pricing Service", sourceId: "APS-14789RAB6-20260630", asOf: "Jun 30, 2026 16:05 ET", condition: "confirmed", detail: "The same price file the statutory holding reads — one instrument, one price" },
            { operator: "×", label: "FX rate", value: "1.0000", source: "Reporting currency is USD", condition: "na", detail: "No conversion applied" },
            { operator: "×", label: "Current face", value: "6,500,000", source: "Custodian holdings file", sourceId: "HRB-POS-20260630", asOf: "Jun 30, 2026", condition: "confirmed", detail: "Agrees to the ledger without explaining items" },
          ],
          result: { label: "Fair value", value: "$6,325,280" },
          rounding: "Half-even · 2 decimal places",
          asOf: "Jun 30, 2026", basis: "GAAP", condition: "confirmed",
          calculationVersion: "VAL 4.1.0", calculatedAt: "Jun 30, 2026 22:12 UTC",
          executedBy: "Valuation engine", engine: "meridian-valuation@4.1.0",
          reproducibility: "Same price, same face, same engine version as the general-account holding. If the two holdings ever report different prices for this instrument, the pricing hierarchy has been overridden on one of them.",
        },
        sources: [
          { label: "Price", value: "Anchorage Pricing Service", mono: false },
          { label: "Quantity", value: "Harbor Institutional Trust", mono: false },
          { label: "Unit valuation", value: "Separate account accounting", mono: false },
          { label: "Data steward", value: "Priya Raman", mono: false },
        ],
        items: [
          { name: "Custodian position file — Jun 30, 2026", kind: "Statement", received: "Jul 1, 2026 04:10 UTC", condition: "confirmed", note: "HRB-POS-20260630" },
          { name: "Evaluated price file — Jun 30, 2026", kind: "Vendor feed", received: "Jun 30, 2026 21:40 UTC", condition: "confirmed", note: "APS daily evaluated bid" },
          { name: "Unit valuation pack — Jun 30, 2026", kind: "Working paper", received: "Jul 1, 2026 06:15 UTC", condition: "confirmed", note: "SA-A unit value $18.4412" },
          { name: "Trade confirmation — Feb 19, 2026", kind: "Confirmation", received: "Feb 19, 2026 17:30 UTC", condition: "confirmed", note: "Broker confirm on file" },
        ],
        overrides: "None. No figure on this position is operator-overridden.",
      },
    },
  ];

  // ── The comparison set (§5 / §18) ───────────────────────────────────────────
  // One row per measure, one column per holding, identical row order in both directions. The
  // scope column is what stops a reader mistaking a contribution for an instrument property.
  var COMPARE = [
    { label: "Current face", scope: "position", key: "quantityLabel" },
    { label: "Market value", scope: "position", values: ["$17,516,160", "$6,325,280"] },
    { label: "Carrying value", scope: "position", values: ["$17,283,072", "$6,325,280"] },
    { label: "Accounting basis", scope: "position", values: ["Statutory · SSAP 43R", "GAAP · ASC 320"] },
    { label: "Measurement", scope: "position", values: ["Amortized cost", "Fair value"] },
    { label: "Unrealized gain", scope: "position", values: ["+$233,088", "+$280,280"], note: ["Disclosure only", "Through earnings"] },
    { label: "Cost basis", scope: "position", values: ["$16,838,740", "$6,045,000"] },
    { label: "NAIC designation", scope: "position", values: ["1.C", "n/a"] },
    { label: "Accrued interest", scope: "position", values: ["$9,375", "$3,385"] },
    { label: "Income YTD", scope: "position", values: ["$411,350", "$99,132"] },
    { label: "Weight", scope: "portfolio", values: ["0.82%", "1.54%"] },
    { label: "Duration contribution", scope: "portfolio", values: ["+0.031 yrs", "+0.059 yrs"] },
    { label: "Position DV01", scope: "position", values: ["$6,726", "$2,429"] },
    { label: "Effective duration", scope: "security", values: ["3.84 yrs", "3.84 yrs"], note: ["Instrument property", "Instrument property"] },
  ];

  var API = {
    security: SECURITY,
    positions: POSITIONS,
    compare: COMPARE,
    byId: function (id) { for (var i = 0; i < POSITIONS.length; i++) if (POSITIONS[i].id === id) return POSITIONS[i]; return null; },
    forSecurity: function (securityId) { return POSITIONS.filter(function (p) { return p.securityId === securityId; }); },
    totals: { face: "24,500,000", marketValue: "$23,841,440", portfolios: 2 },
  };

  global.POSITION_DATA = API;
})(typeof window !== "undefined" ? window : globalThis);
