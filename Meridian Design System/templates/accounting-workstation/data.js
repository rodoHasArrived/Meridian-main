// Accounting Workstation — seed data module (ES module, dynamically imported by the DC logic).
// All figures are internally consistent: the ledger pairs balance, the trial balance proves
// Σdebit = Σcredit, the reconciliation variance equals the one unmatched statement line, and
// the FX rows net to the revaluation entry the workstation prefills.

export const OPENINGS = { "1100": 120000 };

// General ledger postings — every ref is a balanced Dr/Cr pair. JE-1072 is a voided duplicate
// (struck through and excluded from totals by LedgerTable).
export const SEED_LEDGER = [
  { date: "06-02", ref: "JE-1042", account: "1100 · Cash", memo: "Client settlement — AAPL block", debit: 82440, credit: "" },
  { date: "06-02", ref: "JE-1042", account: "1200 · Brokerage receivable", memo: "Client settlement — AAPL block", debit: "", credit: 82440 },
  { date: "06-03", ref: "JE-1043", account: "5100 · Commissions & fees", memo: "Commission & exchange fees", debit: 318.5, credit: "" },
  { date: "06-03", ref: "JE-1043", account: "1100 · Cash", memo: "Commission & exchange fees", debit: "", credit: 318.5 },
  { date: "06-05", ref: "JE-1051", account: "1500 · Custody account", memo: "Wire to custodian", debit: 40000, credit: "" },
  { date: "06-05", ref: "JE-1051", account: "1100 · Cash", memo: "Wire to custodian", debit: "", credit: 40000 },
  { date: "06-09", ref: "JE-1060", account: "1200 · Brokerage receivable", memo: "Dividend receivable — MSFT", debit: 1290, credit: "" },
  { date: "06-09", ref: "JE-1060", account: "4200 · Dividend & interest income", memo: "Dividend receivable — MSFT", debit: "", credit: 1290 },
  { date: "06-12", ref: "JE-1071", account: "5300 · Financing cost", memo: "Margin interest — June", debit: 1120, credit: "" },
  { date: "06-12", ref: "JE-1071", account: "1100 · Cash", memo: "Margin interest — June", debit: "", credit: 1120 },
  { date: "06-13", ref: "JE-1072", account: "5100 · Commissions & fees", memo: "Duplicate of JE-1043 — voided", debit: 318.5, credit: "", status: "void" },
  { date: "06-13", ref: "JE-1072", account: "1100 · Cash", memo: "Duplicate of JE-1043 — voided", debit: "", credit: 318.5, status: "void" },
  { date: "06-16", ref: "JE-1080", account: "1310 · Equities", memo: "Buy 40 NVDA @ 1,603.00", debit: 64120, credit: "" },
  { date: "06-16", ref: "JE-1080", account: "1100 · Cash", memo: "Buy 40 NVDA @ 1,603.00", debit: "", credit: 64120 },
];

// Chart of accounts — leaf balances are signed debit-positive (liability/equity/revenue carry
// negative signs); parents roll up in AccountTree. Cash agrees with the ledger closing balance
// (120,000.00 opening − 23,118.50 net June movement, voids excluded).
export const COA = [
  { code: "1000", name: "Assets", type: "asset", children: [
    { code: "1100", name: "Cash & equivalents", type: "asset", balance: 96881.5 },
    { code: "1200", name: "Brokerage receivable", type: "asset", balance: 42580 },
    { code: "1500", name: "Custody account", type: "asset", balance: 40000 },
    { code: "1300", name: "Marketable securities", type: "asset", children: [
      { code: "1310", name: "Equities", type: "asset", balance: 884002 },
      { code: "1320", name: "Fixed income", type: "asset", balance: 312500 },
      { code: "1330", name: "Derivatives", type: "asset", balance: 18240 },
    ]},
  ]},
  { code: "2000", name: "Liabilities", type: "liability", children: [
    { code: "2100", name: "Margin payable", type: "liability", balance: -210000 },
    { code: "2200", name: "Accrued fees", type: "liability", balance: -3850 },
    { code: "2300", name: "Taxes payable", type: "liability", balance: -64120 },
  ]},
  { code: "3000", name: "Equity", type: "equity", children: [
    { code: "3100", name: "Contributed capital", type: "equity", balance: -700000 },
    { code: "3200", name: "Retained earnings", type: "equity", balance: -54153.5 },
  ]},
  { code: "4000", name: "Revenue", type: "revenue", children: [
    { code: "4100", name: "Realized trading gains", type: "revenue", balance: -412800 },
    { code: "4200", name: "Dividend & interest income", type: "revenue", balance: -38420 },
  ]},
  { code: "5000", name: "Expenses", type: "expense", children: [
    { code: "5100", name: "Commissions & fees", type: "expense", balance: 28440 },
    { code: "5200", name: "Data & infrastructure", type: "expense", balance: 19500 },
    { code: "5300", name: "Financing cost", type: "expense", balance: 41200 },
  ]},
];

// Flatten COA leaves into TrialBalance rows — `balance` signed in normal-side terms
// (credit-normal types flip sign so a healthy liability sits in the credit column).
const CREDIT_NORMAL = { liability: 1, equity: 1, revenue: 1 };
export function buildTbRows(nodes) {
  const out = [];
  const walk = (n) => {
    if (n.children && n.children.length) { n.children.forEach(walk); return; }
    const flip = CREDIT_NORMAL[n.type] ? -1 : 1;
    out.push({ code: n.code, account: n.name, type: n.type, balance: (Number(n.balance) || 0) * flip });
  };
  nodes.forEach(walk);
  return out;
}
export const TB_ROWS = buildTbRows(COA);

// Statement of operations — Q2 vs Q1 comparison. Totals agree with the COA revenue/expense nets.
export const STMT_SECTIONS = [
  { label: "Revenue", rows: [
    { label: "Realized trading gains", values: { q2: 412800, q1: 388110 } },
    { label: "Dividend & interest income", values: { q2: 38420, q1: 35900 } },
  ], subtotal: { label: "Total revenue", values: { q2: 451220, q1: 424010 } } },
  { label: "Operating expenses", rows: [
    { label: "Commissions & fees", values: { q2: -28440, q1: -26900 } },
    { label: "Data & infrastructure", values: { q2: -19500, q1: -19500 } },
    { label: "Financing cost", values: { q2: -41200, q1: -38750 }, indent: 1, muted: true },
  ], subtotal: { label: "Total operating expenses", values: { q2: -89140, q1: -85150 } } },
];
export const STMT_TOTAL = { label: "Net income", values: { q2: 362080, q1: 338860 } };

// Cash reconciliation — account 1100, June. One statement line (INT-0210, +142.18) has no
// ledger counterpart; posting the prefilled accrual entry clears it.
export const REC_LEFT = [
  { id: 1, date: "06-02", ref: "DTC-88421", memo: "Settlement credit", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "FEE-0093", memo: "Commission", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "WIRE-5510", memo: "Custodian wire", amount: -40000, matched: true },
  { id: 4, date: "06-06", ref: "INT-0210", memo: "Interest accrual", amount: 142.18, matched: false },
  { id: 5, date: "06-12", ref: "MRG-0612", memo: "Margin interest", amount: -1120, matched: true },
  { id: 6, date: "06-16", ref: "EQT-7810", memo: "Equity purchase — NVDA", amount: -64120, matched: true },
];
export const REC_RIGHT = [
  { id: 1, date: "06-02", ref: "JE-1042", memo: "Client settlement", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "JE-1043", memo: "Commission & fees", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "JE-1051", memo: "Wire to custodian", amount: -40000, matched: true },
  { id: 5, date: "06-12", ref: "JE-1071", memo: "Margin interest", amount: -1120, matched: true },
  { id: 6, date: "06-16", ref: "JE-1080", memo: "Buy 40 NVDA", amount: -64120, matched: true },
];

// Tax lots — proceeds differ slightly from market value (actual sale prints).
export const LOTS = [
  { acquired: "2024-11-03", symbol: "MSFT", quantity: 120, costBasis: 49200, marketValue: 53880, proceeds: 54110 },
  { acquired: "2025-02-14", symbol: "AAPL", quantity: 400, costBasis: 68240, marketValue: 79120, proceeds: 78650 },
  { acquired: "2026-01-09", symbol: "NVDA", quantity: 150, costBasis: 121500, marketValue: 138900, proceeds: 139420 },
  { acquired: "2026-03-17", symbol: "TLT", quantity: 320, costBasis: 28227, marketValue: 29136, proceeds: 29010 },
  { acquired: "2026-04-22", symbol: "TSLA", quantity: 200, costBasis: 49800, marketValue: 44120, proceeds: 43890 },
];

// FX revaluation — rates are base units per 1 local unit. Nets to +1,828.00 USD.
export const FX_ROWS = [
  { currency: "EUR", account: "Prime broker cash", local: 1250000, bookedRate: 1.0842, currentRate: 1.0918 },
  { currency: "GBP", account: "Margin posted — LSE", local: 420000, bookedRate: 1.2704, currentRate: 1.2652 },
  { currency: "JPY", account: "Futures margin — OSE", local: 98000000, bookedRate: 0.006701, currentRate: 0.006645 },
];

export const ACCOUNT_OPTIONS = [
  "1100 · Cash", "1200 · Brokerage receivable", "1310 · Equities",
  "1400 · FX translation adjustment", "1500 · Custody account",
  "2100 · Margin payable", "2200 · Accrued fees",
  "4100 · Realized trading gains", "4200 · Dividend & interest income",
  "4300 · Unrealized FX gain/loss",
  "5100 · Commissions & fees", "5200 · Data & infrastructure", "5300 · Financing cost",
];
