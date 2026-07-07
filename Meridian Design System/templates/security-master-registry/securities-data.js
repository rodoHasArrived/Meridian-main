// Meridian — Security Master demo data. Plain script (window global), not a module.
// One row per asset-class family shows the full breadth of the entity-inspector pattern:
// bond (fixed + floating), derivative (swap + FX forward), LP, structured (MBS/CLO/ABS), loan.
(function () {
  function complianceChecks(c) {
    return [
      { code: "CMP-SANCTIONS", severity: c.sanctionsScreened ? "Ready" : "Blocked",
        message: c.sanctionsScreened ? "Sanctions screening cleared" : "Sanctions screening not completed" },
      { code: "CMP-WATCHLIST", severity: c.watchlist ? "Action" : "Ready",
        message: c.watchlist ? "Entity appears on internal watchlist — review required" : "No watchlist matches" },
      { code: "CMP-RESTRICTED", severity: c.restricted ? "Blocked" : "Ready",
        message: c.restricted ? "Security is on the restricted list" : "Not on the restricted list" },
    ];
  }

  var SECURITIES = [
    {
      id: "ust-4-25-29", name: "United States Treasury Note 4.25% 2029", shortCode: "UST 4.25 29",
      securityId: "0X9T2828QG8UST00291129FIXED", cusip: "91282CJL6", isin: "US91282CJL60", sedol: "—", ticker: "T 4.25 11/29",
      issuer: "U.S. Department of the Treasury", assetClass: "Government Bond", currency: "USD",
      issueDate: "2024-11-15", maturityDate: "2029-11-15", sector: "Government", rating: "AAA", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0X9T2828QG8UST00291129FIXED" }, { label: "CUSIP", value: "91282CJL6" },
          { label: "ISIN", value: "US91282CJL60" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "T 4.25 11/29" }, { label: "Issuer", value: "U.S. Department of the Treasury", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2024-11-15" }, { label: "Maturity Date", value: "2029-11-15" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Senior Unsecured" }, { label: "Collateral Type", value: "—" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Sovereign" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Fixed" }, { label: "Issuance Size", value: "$42,000,000,000" },
          { label: "Outstanding Amount", value: "$42,000,000,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "Actual/Actual" }, { label: "Coupon Frequency", value: "Semiannual" },
          { label: "Business Day Convention", value: "Following" }, { label: "Payment Calendar", value: "US Federal" },
          { label: "Settlement Days", value: "1" }, { label: "Ex-Dividend Days", value: "—" } ] },
        { type: "checklist", title: "Optionality & Features", items: [
          { label: "Callable", checked: false }, { label: "Putable", checked: false },
          { label: "Convertible", checked: false }, { label: "Floating Rate", checked: false },
          { label: "Amortization", checked: false }, { label: "Sinking Fund", checked: false } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "4.18" }, { label: "Yield to Worst (%)", value: "4.18" },
          { label: "Duration", value: "3.90" }, { label: "Modified Duration", value: "3.82" },
          { label: "Convexity", value: "0.19" }, { label: "DV01", value: "$3,820" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Government" }, { label: "Industry", value: "Sovereign Debt" },
          { label: "Accounting Classification", value: "HTM" }, { label: "Tax Classification", value: "Exempt (state)" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "Federal Reserve Bank of New York" }, { label: "Data Steward", value: "Priya Anand" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 1" }, { label: "Pricing Source", value: "Bloomberg BVAL" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "AAA" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [
        { code: "VAL-014", severity: "Ready", message: "Price sourced within tolerance of BVAL composite" },
        { code: "VAL-002", severity: "Ready", message: "Coupon schedule reconciles to issuance terms" },
      ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-06-02", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [ { author: "D. Kim", ts: "2026-05-11 09:02Z", text: "Benchmark 5Y — used as OAS reference for IG corporates." } ],
      audit: [
        { ts: "2026-06-02 08:14Z", user: "E. Torres", field: "Sanctions screening", oldValue: "Pending", newValue: "Cleared" },
        { ts: "2024-11-15 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" },
      ],
    },
    {
      id: "aapl-4-5-25", name: "Apple Inc. 4.50% Senior Notes 2025", shortCode: "AAPL 4.5 25",
      securityId: "0X8A0378QBM9AAPL04502505FIX", cusip: "037833BM9", isin: "US037833BM97", sedol: "2000902", ticker: "AAPL 4.5 05/25",
      issuer: "Apple Inc.", assetClass: "Corp Bond", currency: "USD",
      issueDate: "2020-05-14", maturityDate: "2025-05-14", sector: "Technology", rating: "AA+", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0X8A0378QBM9AAPL04502505FIX" }, { label: "CUSIP", value: "037833BM9" },
          { label: "ISIN", value: "US037833BM97" }, { label: "SEDOL", value: "2000902" },
          { label: "Ticker", value: "AAPL 4.5 05/25" }, { label: "Issuer", value: "Apple Inc.", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2020-05-14" }, { label: "Maturity Date", value: "2025-05-14" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Senior Unsecured" }, { label: "Collateral Type", value: "—" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Corporate" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Fixed" }, { label: "Issuance Size", value: "$2,000,000,000" },
          { label: "Outstanding Amount", value: "$2,000,000,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "30/360" }, { label: "Coupon Frequency", value: "Semiannual" },
          { label: "Business Day Convention", value: "Modified Following" }, { label: "Payment Calendar", value: "NYC" },
          { label: "Settlement Days", value: "2" }, { label: "Ex-Dividend Days", value: "—" } ] },
        { type: "checklist", title: "Optionality & Features", items: [
          { label: "Callable", checked: false }, { label: "Putable", checked: false },
          { label: "Convertible", checked: false }, { label: "Floating Rate", checked: false },
          { label: "Amortization", checked: false }, { label: "Sinking Fund", checked: false } ] },
        { type: "callout", tone: "warning", title: "Maturity inside 12 months",
          text: "Confirm roll or redemption instruction before 2025-05-14." },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "4.61" }, { label: "Yield to Worst (%)", value: "4.61" },
          { label: "Duration", value: "0.85" }, { label: "Modified Duration", value: "0.83" },
          { label: "Convexity", value: "0.02" }, { label: "DV01", value: "$1,660" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Technology" }, { label: "Industry", value: "Consumer Electronics" },
          { label: "Accounting Classification", value: "AFS" }, { label: "Tax Classification", value: "Taxable" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "BNY Mellon" }, { label: "Data Steward", value: "Christopher Davis" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "AA+" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [
        { code: "VAL-002", severity: "Ready", message: "Coupon schedule reconciles to issuance terms" },
        { code: "VAL-021", severity: "Review", message: "Maturity inside 12 months — confirm roll or redemption instruction" },
      ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-05-20", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [ { author: "M. Alvarez", ts: "2026-04-02 15:40Z", text: "Confirmed no make-whole call exercised; holding to maturity." } ],
      audit: [ { ts: "2026-05-20 11:00Z", user: "E. Torres", field: "Compliance review", oldValue: "—", newValue: "Cleared" } ],
    },
    {
      id: "bac-frn-sofr-27", name: "Bank of America Corporation 6M SOFR FRN 2027", shortCode: "BAC FRN SOFR 27",
      securityId: "0X6B051GJV2BAC6MSOFR2905FRN", cusip: "06051GJV2", isin: "US06051GJV24", sedol: "—", ticker: "BAC FRN 05/29",
      issuer: "Bank of America Corporation", assetClass: "Corp Bond", currency: "USD",
      issueDate: "2024-01-25", maturityDate: "2029-05-22", sector: "Financial Services", rating: "A+", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0X6B051GJV2BAC6MSOFR2905FRN" }, { label: "CUSIP", value: "06051GJV2" },
          { label: "ISIN", value: "US06051GJV24" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "BAC FRN 05/29" }, { label: "Issuer", value: "Bank of America Corporation", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2024-01-25" }, { label: "Maturity Date", value: "2029-05-22" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Senior Unsecured" }, { label: "Collateral Type", value: "—" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Corporate" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Floating" }, { label: "Issuance Size", value: "$1,500,000,000" },
          { label: "Outstanding Amount", value: "$1,500,000,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "Actual/360" }, { label: "Coupon Frequency", value: "Quarterly" },
          { label: "Business Day Convention", value: "Modified Following" }, { label: "Payment Calendar", value: "NYC" },
          { label: "Settlement Days", value: "2" }, { label: "Ex-Dividend Days", value: "—" } ] },
        { type: "fields", title: "Floating Rate Details", fields: [
          { label: "Floating Index", value: "SOFR" }, { label: "Index Tenor", value: "6M" },
          { label: "Spread (bps)", value: "95" }, { label: "Floor (%)", value: "0" },
          { label: "Cap (%)", value: "—" }, { label: "Reset Frequency", value: "Quarterly" },
          { label: "Lookback Days", value: "2" }, { label: "Lockout Days", value: "—" } ] },
        { type: "checklist", title: "Optionality & Features", items: [
          { label: "Callable", checked: false }, { label: "Putable", checked: false },
          { label: "Convertible", checked: false }, { label: "Floating Rate", checked: true },
          { label: "Amortization", checked: false }, { label: "Sinking Fund", checked: false } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "—" }, { label: "Yield to Worst (%)", value: "—" },
          { label: "Duration", value: "0.25" }, { label: "Modified Duration", value: "0.24" },
          { label: "Convexity", value: "—" }, { label: "DV01", value: "—" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Banking" },
          { label: "Accounting Classification", value: "AFS" }, { label: "Tax Classification", value: "Taxable" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "BNY Mellon" }, { label: "Data Steward", value: "Christopher Davis" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "A+" },
          { label: "Manual Price Override", value: "—" } ] },
        { type: "schedule", title: "Coupon & Principal Payment Schedule", rows: [
          { eventType: "Coupon", paymentDate: "2026-08-25", index: "SOFR", spread: "+95bps", expected: "$427,013.89", actual: "—", posted: false },
          { eventType: "Coupon", paymentDate: "2026-05-25", index: "SOFR", spread: "+95bps", expected: "$421,296.30", actual: "$421,296.30", posted: true },
          { eventType: "Coupon", paymentDate: "2026-02-25", index: "SOFR", spread: "+95bps", expected: "$418,061.11", actual: "$418,061.11", posted: true },
          { eventType: "Coupon", paymentDate: "2025-11-25", index: "SOFR", spread: "+95bps", expected: "$425,617.78", actual: "$425,617.78", posted: true },
        ] },
      ],
      validation: [
        { code: "VAL-002", severity: "Ready", message: "Coupon schedule reconciles to issuance terms — 3 of 4 events posted" },
        { code: "VAL-018", severity: "Action", message: "Next reset 2026-08-25 — SOFR fixing not yet published" },
      ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-06-09", reviewedBy: "Compliance — A. Rodriguez", status: "Ready" },
      notesSeed: [ { author: "A. Rodriguez", ts: "2026-06-09 14:02Z", text: "Confirmed floating-rate reset calc against SOFR fixings; no discrepancy." } ],
      audit: [
        { ts: "2026-06-09 14:02Z", user: "A. Rodriguez", field: "Compliance review", oldValue: "—", newValue: "Cleared" },
        { ts: "2026-05-25 12:00Z", user: "system", field: "Coupon posted", oldValue: "Scheduled", newValue: "Posted · $421,296.30" },
        { ts: "2024-01-25 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" },
      ],
    },
    {
      id: "barc-irs-sofr", name: "Barclays SOFR/Fixed Interest Rate Swap", shortCode: "BARC IRS SOFR/FIX",
      securityId: "0XDBARCIRSSOFRFIX20310521SW", cusip: "—", isin: "—", sedol: "—", ticker: "—",
      issuer: "Barclays Bank PLC", assetClass: "Derivative", currency: "USD",
      issueDate: "2024-01-09", maturityDate: "2031-05-21", sector: "Financial Services", rating: "—", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XDBARCIRSSOFRFIX20310521SW" }, { label: "CUSIP", value: "—" },
          { label: "ISIN", value: "—" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "—" }, { label: "Issuer", value: "Barclays Bank PLC", accent: true } ] },
        { type: "fields", title: "Contract Details", fields: [
          { label: "Product Type", value: "Interest Rate Swap" }, { label: "Trade Date", value: "2024-01-09" },
          { label: "Effective Date", value: "2024-01-11" }, { label: "Termination Date", value: "2031-05-21" },
          { label: "Notional Amount", value: "$50,000,000" }, { label: "Currency", value: "USD" },
          { label: "Settlement Type", value: "Cash" }, { label: "Pay / Receive", value: "Pay Fixed 3.65% / Receive SOFR" } ] },
        { type: "fields", title: "Counterparty Risk & Credit Support", fields: [
          { label: "Counterparty", value: "Barclays Bank PLC" }, { label: "Credit Support Annex (CSA)", value: "On file" },
          { label: "Collateral Posted", value: "$1,240,000" }, { label: "Initial Margin", value: "$620,000" },
          { label: "Variation Margin", value: "$310,000" }, { label: "Independent Amount", value: "$0" } ] },
        { type: "fields", title: "Exposure Metrics & Risk", fields: [
          { label: "Current Exposure", value: "$1,240,000" }, { label: "Potential Future Exposure", value: "$3,880,000" },
          { label: "Replacement Cost", value: "$1,240,000" }, { label: "CVA", value: "$18,400" }, { label: "DVA", value: "$6,200" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Banking" },
          { label: "Accounting Classification", value: "Trading" }, { label: "Tax Classification", value: "N/A" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "—" }, { label: "Data Steward", value: "Alexandra Rodriguez" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Internal — QuantLib curve" },
          { label: "Valuation Method", value: "Discounted Cash Flow" }, { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [
        { code: "VAL-030", severity: "Ready", message: "CSA terms match legal confirmation on file" },
        { code: "VAL-031", severity: "Ready", message: "Curve inputs refreshed within 1 hour" },
      ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-06-01", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [ { author: "A. Rodriguez", ts: "2026-05-30 10:11Z", text: "Reconfirmed collateral schedule against ISDA CSA Annex III." } ],
      audit: [ { ts: "2026-06-01 09:30Z", user: "E. Torres", field: "Compliance review", oldValue: "—", newValue: "Cleared" } ],
    },
    {
      id: "bx-refviii", name: "Blackstone Real Estate Partners Fund VIII, L.P.", shortCode: "BX REIF VIII",
      securityId: "0XLBXREIF8REALESTATE2022LP", cusip: "—", isin: "—", sedol: "—", ticker: "—",
      issuer: "Blackstone Group Inc.", assetClass: "LP", currency: "USD",
      issueDate: "2022-05-31", maturityDate: "—", sector: "Real Estate", rating: "—", flags: ["Restricted"],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XLBXREIF8REALESTATE2022LP" }, { label: "CUSIP", value: "—" },
          { label: "ISIN", value: "—" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "—" }, { label: "Issuer", value: "Blackstone Group Inc.", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Commitment Date", value: "2022-05-31" }, { label: "Fund Term", value: "Open-ended (10yr + 2×1yr)" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "General Partner", value: "Blackstone Group Inc." }, { label: "Issuer Type", value: "General Partner" } ] },
        { type: "fields", title: "Commitment Details", fields: [
          { label: "Vintage Year", value: "2022" }, { label: "Commitment Amount", value: "$25,000,000" },
          { label: "Called Capital", value: "$16,750,000" }, { label: "Uncalled Capital", value: "$8,250,000" },
          { label: "Distributions", value: "$4,120,000" }, { label: "Net IRR", value: "14.2%" },
          { label: "Strategy", value: "Opportunistic real estate", accent: false } ] },
        { type: "callout", tone: "danger", title: "Trading restricted",
          text: "Restricted list — Real Estate desk. No secondary transfer without Compliance sign-off (added 2025-11-02)." },
        { type: "checklist", title: "ESG & Sustainability", items: [
          { label: "Green Bond", checked: false }, { label: "Social Bond", checked: false },
          { label: "Sustainability-Linked", checked: true } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Real Estate" }, { label: "Industry", value: "Diversified Real Estate" },
          { label: "Accounting Classification", value: "Equity Method" }, { label: "Tax Classification", value: "K-1 Pass-through" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "State Street — Alternative Investments" }, { label: "Data Steward", value: "Priya Anand" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: true }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 3" }, { label: "Pricing Source", value: "GP NAV statement (Q1 2026)" },
          { label: "Valuation Method", value: "Net Asset Value" }, { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [
        { code: "VAL-041", severity: "Action", message: "NAV statement is 92 days old — request updated capital account statement" },
        { code: "VAL-005", severity: "Blocked", message: "Restricted-list flag requires Compliance sign-off before any transfer" },
      ],
      compliance: { restricted: true, sanctionsScreened: true, watchlist: true, lastReview: "2025-11-02", reviewedBy: "Compliance — E. Torres", status: "Blocked" },
      notesSeed: [ { author: "E. Torres", ts: "2025-11-02 16:45Z", text: "Added to restricted list pending review of GP-affiliated co-investment." } ],
      audit: [
        { ts: "2025-11-02 16:45Z", user: "E. Torres", field: "Restricted flag", oldValue: "false", newValue: "true" },
        { ts: "2022-05-31 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" },
      ],
    },
    {
      id: "eurusd-fwd-citi", name: "EUR/USD FX Forward Contract", shortCode: "EURUSD FWD 6M",
      securityId: "0XDEURUSDFWDCITI20261122FX", cusip: "—", isin: "—", sedol: "—", ticker: "—",
      issuer: "Citibank N.A.", assetClass: "Derivative", currency: "USD",
      issueDate: "2026-05-22", maturityDate: "2026-11-22", sector: "Financial Services", rating: "—", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XDEURUSDFWDCITI20261122FX" }, { label: "CUSIP", value: "—" },
          { label: "ISIN", value: "—" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "—" }, { label: "Issuer", value: "Citibank N.A.", accent: true } ] },
        { type: "fields", title: "Contract Details", fields: [
          { label: "Product Type", value: "FX Forward" }, { label: "Trade Date", value: "2026-05-22" },
          { label: "Near Date", value: "2026-05-22" }, { label: "Far Date", value: "2026-11-22" },
          { label: "Buy / Sell", value: "Buy EUR / Sell USD" }, { label: "Notional (Buy)", value: "€8,140,000" },
          { label: "Notional (Sell)", value: "$8,762,900" }, { label: "Forward Rate / Spot Rate", value: "1.0765 / 1.0810" } ] },
        { type: "fields", title: "Counterparty Risk & Credit Support", fields: [
          { label: "Counterparty", value: "Citibank N.A." }, { label: "Credit Support Annex (CSA)", value: "On file" },
          { label: "Collateral Posted", value: "$0" }, { label: "Initial Margin", value: "$0" },
          { label: "Variation Margin", value: "−$61,700" }, { label: "Independent Amount", value: "$87,400" } ] },
        { type: "callout", tone: "danger", title: "Wrong-way risk flagged",
          text: "Exposure to Citibank N.A. increases as EUR/USD moves against the position — the bilateral CSA reduces but does not eliminate this." },
        { type: "fields", title: "Exposure Metrics & Risk", fields: [
          { label: "Current Exposure", value: "$61,700" }, { label: "Potential Future Exposure", value: "$136,900" },
          { label: "Replacement Cost", value: "$61,700" }, { label: "CVA", value: "$2,180" }, { label: "DVA", value: "$740" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Banking" },
          { label: "Accounting Classification", value: "Trading" }, { label: "Tax Classification", value: "N/A" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "—" }, { label: "Data Steward", value: "Alexandra Rodriguez" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Forward Curve" }, { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [
        { code: "VAL-030", severity: "Ready", message: "CSA terms match legal confirmation on file" },
        { code: "VAL-050", severity: "Action", message: "Wrong-way risk flagged — quarterly counterparty review due 2026-07-15" },
      ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-05-22", reviewedBy: "Compliance — E. Torres", status: "Review" },
      notesSeed: [ { author: "A. Rodriguez", ts: "2026-05-22 10:05Z",
        text: "FX forward hedges EUR-denominated bond coupon. Sell EUR spot 1.0810, contract 1.0765 — mark reflects USD strength." } ],
      audit: [ { ts: "2026-05-22 10:05Z", user: "A. Rodriguez", field: "Record created", oldValue: "—", newValue: "—" } ],
    },
    {
      id: "bund-1-7-32", name: "Federal Republic of Germany 1.70% 2032", shortCode: "BUND 1.7 32",
      securityId: "0XGDE0001102309BUND17032AN", cusip: "—", isin: "DE0001102309", sedol: "BYXPYS0", ticker: "DBR 1.7 08/32",
      issuer: "Federal Republic of Germany", assetClass: "Government Bond", currency: "EUR",
      issueDate: "2022-02-22", maturityDate: "2032-08-15", sector: "Government", rating: "AAA", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XGDE0001102309BUND17032AN" }, { label: "CUSIP", value: "—" },
          { label: "ISIN", value: "DE0001102309" }, { label: "SEDOL", value: "BYXPYS0" },
          { label: "Ticker", value: "DBR 1.7 08/32" }, { label: "Issuer", value: "Federal Republic of Germany", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2022-02-22" }, { label: "Maturity Date", value: "2032-08-15" },
          { label: "Currency", value: "EUR" }, { label: "Country", value: "Germany" },
          { label: "Seniority Level", value: "Senior Unsecured" }, { label: "Collateral Type", value: "—" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Sovereign" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Fixed" }, { label: "Issuance Size", value: "€5,000,000,000" },
          { label: "Outstanding Amount", value: "€5,000,000,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "Actual/Actual (ICMA)" }, { label: "Coupon Frequency", value: "Annual" },
          { label: "Business Day Convention", value: "Following" }, { label: "Payment Calendar", value: "TARGET2" },
          { label: "Settlement Days", value: "2" }, { label: "Ex-Dividend Days", value: "—" } ] },
        { type: "checklist", title: "Optionality & Features", items: [
          { label: "Callable", checked: false }, { label: "Putable", checked: false },
          { label: "Convertible", checked: false }, { label: "Floating Rate", checked: false },
          { label: "Amortization", checked: false }, { label: "Sinking Fund", checked: false } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "2.31" }, { label: "Yield to Worst (%)", value: "2.31" },
          { label: "Duration", value: "5.40" }, { label: "Modified Duration", value: "5.31" },
          { label: "Convexity", value: "0.34" }, { label: "DV01", value: "€2,660" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Government" }, { label: "Industry", value: "Sovereign Debt" },
          { label: "Accounting Classification", value: "HTM" }, { label: "Tax Classification", value: "Exempt" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "Clearstream Banking" }, { label: "Data Steward", value: "Priya Anand" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 1" }, { label: "Pricing Source", value: "Bloomberg BVAL" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "AAA" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [ { code: "VAL-002", severity: "Ready", message: "Coupon schedule reconciles to issuance terms" } ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-04-18", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [],
      audit: [ { ts: "2022-02-22 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" } ],
    },
    {
      id: "fhlmc-sd8123", name: "Freddie Mac Pool SD8123 4.50%", shortCode: "FHLMC SD8123",
      securityId: "0XM3132D0SD8FHLMC45020415AY", cusip: "3132D0SD8", isin: "US3132D0SD87", sedol: "—", ticker: "FHLMC SD8123",
      issuer: "Federal Home Loan Mortgage Corporation", assetClass: "MBS", currency: "USD",
      issueDate: "2020-05-31", maturityDate: "2041-05-21", sector: "Financial Services", rating: "AAA", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XM3132D0SD8FHLMC45020415AY" }, { label: "CUSIP", value: "3132D0SD8" },
          { label: "ISIN", value: "US3132D0SD87" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "FHLMC SD8123" }, { label: "Issuer", value: "Federal Home Loan Mortgage Corporation", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2020-05-31" }, { label: "Maturity Date", value: "2041-05-21" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Senior" }, { label: "Collateral Type", value: "30Y Fixed-Rate Conforming Mortgages" },
          { label: "Guarantor", value: "Freddie Mac" }, { label: "Issuer Type", value: "GSE" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Fixed (Pass-through)" }, { label: "Issuance Size", value: "$312,000,000" },
          { label: "Outstanding Amount", value: "$118,640,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "30/360" }, { label: "Coupon Frequency", value: "Monthly" },
          { label: "Business Day Convention", value: "Following" }, { label: "Payment Calendar", value: "US Federal" },
          { label: "Settlement Days", value: "2" }, { label: "Pool Factor", value: "38.03%" } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "4.72" }, { label: "Yield to Worst (%)", value: "4.61" },
          { label: "Duration", value: "4.60" }, { label: "Modified Duration", value: "4.50" },
          { label: "Convexity", value: "−0.85 (prepay risk)" }, { label: "DV01", value: "$5,340" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Agency MBS" },
          { label: "Accounting Classification", value: "AFS" }, { label: "Tax Classification", value: "Taxable" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "BNY Mellon" }, { label: "Data Steward", value: "Christopher Davis" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Matrix Price" }, { label: "Rating", value: "AAA" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [ { code: "VAL-060", severity: "Review", message: "Pool factor updated — 38.03% of original balance outstanding" } ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-05-01", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [],
      audit: [ { ts: "2026-05-21 00:00Z", user: "system", field: "Pool factor", oldValue: "38.41%", newValue: "38.03%" } ],
    },
    {
      id: "octagon-clo-23-1a", name: "Octagon Investment Partners CLO 2023-1A Class A", shortCode: "OCTAGON CLO 23-1A A",
      securityId: "0XC675445AA8OCT2023A1CLASSA", cusip: "675445AA8", isin: "US675445AA87", sedol: "—", ticker: "OCT 2023-1A A",
      issuer: "Octagon Investment Partners", assetClass: "CLO", currency: "USD",
      issueDate: "2023-04-19", maturityDate: "2034-05-21", sector: "Financial Services", rating: "AAA", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XC675445AA8OCT2023A1CLASSA" }, { label: "CUSIP", value: "675445AA8" },
          { label: "ISIN", value: "US675445AA87" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "OCT 2023-1A A" }, { label: "Issuer", value: "Octagon Investment Partners", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2023-04-19" }, { label: "Maturity Date", value: "2034-05-21" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Class A — Senior Secured" }, { label: "Collateral Type", value: "Senior Secured Leveraged Loans" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Structured Vehicle" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Floating" }, { label: "Issuance Size", value: "$310,000,000" },
          { label: "Outstanding Amount", value: "$310,000,000" } ] },
        { type: "fields", title: "Floating Rate Details", fields: [
          { label: "Floating Index", value: "SOFR" }, { label: "Index Tenor", value: "3M" },
          { label: "Spread (bps)", value: "138" }, { label: "Floor (%)", value: "0" },
          { label: "Cap (%)", value: "—" }, { label: "Reset Frequency", value: "Quarterly" } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Overcollateralization Test", value: "132.4% (trigger 120%)" },
          { label: "Duration", value: "0.28" }, { label: "Modified Duration", value: "0.27" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Structured Credit" },
          { label: "Accounting Classification", value: "AFS" }, { label: "Tax Classification", value: "Taxable" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "Citibank N.A. — Trustee" }, { label: "Data Steward", value: "Christopher Davis" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "AAA" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [ { code: "VAL-070", severity: "Ready", message: "Overcollateralization test passing at 132.4% (trigger 120%)" } ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-04-19", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [],
      audit: [ { ts: "2023-04-19 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" } ],
    },
    {
      id: "ppl-johnson-001", name: "Private Personal Loan — Johnson, Michael", shortCode: "PPL-JOHNSON-001",
      securityId: "0XLPPLJOHNSONM202306UNSECD", cusip: "—", isin: "—", sedol: "—", ticker: "—",
      issuer: "Individual Borrower", assetClass: "Loan", currency: "USD",
      issueDate: "2023-06-14", maturityDate: "2030-05-21", sector: "Consumer Finance", rating: "Unrated", flags: ["Restricted"],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XLPPLJOHNSONM202306UNSECD" }, { label: "CUSIP", value: "—" },
          { label: "ISIN", value: "—" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "—" }, { label: "Issuer", value: "Individual Borrower", accent: true } ] },
        { type: "fields", title: "Loan Details", fields: [
          { label: "Borrower", value: "Michael Johnson" }, { label: "Loan Type", value: "Unsecured Personal Loan" },
          { label: "Origination Date", value: "2023-06-14" }, { label: "Maturity Date", value: "2030-05-21" },
          { label: "Rate Type", value: "Fixed" }, { label: "Interest Rate", value: "9.25%" },
          { label: "Payment Frequency", value: "Monthly" }, { label: "Collateral", value: "None" },
          { label: "Original Principal", value: "$85,000" }, { label: "Outstanding Principal", value: "$61,420" } ] },
        { type: "callout", tone: "warning", title: "Related-party loan",
          text: "Borrower is a related party of a Meridian principal — restricted from third-party sale or securitization pending disclosure review." },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Consumer Finance" }, { label: "Industry", value: "Personal Lending" },
          { label: "Accounting Classification", value: "Held for Investment" }, { label: "Tax Classification", value: "N/A" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "—" }, { label: "Data Steward", value: "Priya Anand" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: true }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 3" }, { label: "Pricing Source", value: "Internal — amortized cost" },
          { label: "Valuation Method", value: "Amortized Cost" }, { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [ { code: "VAL-080", severity: "Action", message: "Related-party disclosure review due — last completed 2025-06-14" } ],
      compliance: { restricted: true, sanctionsScreened: true, watchlist: false, lastReview: "2025-06-14", reviewedBy: "Compliance — E. Torres", status: "Review" },
      notesSeed: [ { author: "E. Torres", ts: "2025-06-14 09:00Z", text: "Related-party disclosure on file; annual re-certification required." } ],
      audit: [ { ts: "2025-06-14 09:00Z", user: "E. Torres", field: "Restricted flag", oldValue: "false", newValue: "true" } ],
    },
    {
      id: "sofi-abs-24-1a", name: "SoFi Consumer Loan Trust 2024-1 Class A", shortCode: "SOFI ABS 24-1 A",
      securityId: "0XA83404LAA1SOFI20241CLASSA", cusip: "83404LAA1", isin: "US83404LAA15", sedol: "—", ticker: "SOFI 2024-1 A",
      issuer: "SoFi Consumer Loan Trust", assetClass: "ABS", currency: "USD",
      issueDate: "2024-02-14", maturityDate: "2032-05-21", sector: "Financial Services", rating: "AA+", flags: [],
      sections: [
        { type: "fields", title: "Identifiers", fields: [
          { label: "Security ID", value: "0XA83404LAA1SOFI20241CLASSA" }, { label: "CUSIP", value: "83404LAA1" },
          { label: "ISIN", value: "US83404LAA15" }, { label: "SEDOL", value: "—" },
          { label: "Ticker", value: "SOFI 2024-1 A" }, { label: "Issuer", value: "SoFi Consumer Loan Trust", accent: true } ] },
        { type: "fields", title: "Security Details", fields: [
          { label: "Issue Date", value: "2024-02-14" }, { label: "Maturity Date", value: "2032-05-21" },
          { label: "Currency", value: "USD" }, { label: "Country", value: "United States" },
          { label: "Seniority Level", value: "Class A — Senior" }, { label: "Collateral Type", value: "Unsecured Consumer Installment Loans" },
          { label: "Guarantor", value: "—" }, { label: "Issuer Type", value: "Structured Vehicle" } ] },
        { type: "fields", title: "Issuance Details", fields: [
          { label: "Coupon Type", value: "Fixed" }, { label: "Issuance Size", value: "$450,000,000" },
          { label: "Outstanding Amount", value: "$298,600,000" } ] },
        { type: "fields", title: "Fixed Income Conventions", fields: [
          { label: "Day Count Method", value: "30/360" }, { label: "Coupon Frequency", value: "Monthly" },
          { label: "Business Day Convention", value: "Following" }, { label: "Payment Calendar", value: "US Federal" } ] },
        { type: "fields", title: "Analytics & Metrics", fields: [
          { label: "Yield to Maturity (%)", value: "5.44" }, { label: "Yield to Worst (%)", value: "5.30" },
          { label: "Duration", value: "1.90" }, { label: "Modified Duration", value: "1.87" },
          { label: "Convexity", value: "0.05" }, { label: "DV01", value: "$5,590" } ] },
        { type: "fields", title: "Classification", fields: [
          { label: "Sector", value: "Financial Services" }, { label: "Industry", value: "Consumer ABS" },
          { label: "Accounting Classification", value: "AFS" }, { label: "Tax Classification", value: "Taxable" } ] },
        { type: "fields", title: "Operational", fields: [
          { label: "Custodian", value: "U.S. Bank — Trustee" }, { label: "Data Steward", value: "Christopher Davis" } ] },
        { type: "checklist", title: "Compliance Flags", items: [
          { label: "Restricted", checked: false }, { label: "Impaired", checked: false }, { label: "Defaulted", checked: false } ] },
        { type: "fields", title: "Valuation & Pricing", fields: [
          { label: "Fair Value Level", value: "Level 2" }, { label: "Pricing Source", value: "Bloomberg" },
          { label: "Valuation Method", value: "Market Price" }, { label: "Rating", value: "AA+" },
          { label: "Manual Price Override", value: "—" } ] },
      ],
      validation: [ { code: "VAL-090", severity: "Ready", message: "Collateral pool performance within base-case CPR/CDR assumptions" } ],
      compliance: { restricted: false, sanctionsScreened: true, watchlist: false, lastReview: "2026-03-10", reviewedBy: "Compliance — E. Torres", status: "Ready" },
      notesSeed: [],
      audit: [ { ts: "2024-02-14 00:00Z", user: "system", field: "Record created", oldValue: "—", newValue: "—" } ],
    },
  ];

  window.SECURITIES = SECURITIES;
  window.complianceChecks = complianceChecks;
})();
