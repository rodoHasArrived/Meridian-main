// Accounting Workstation — seed data module (ES module, dynamically imported by the DC logic).
// All figures are internally consistent: the ledger pairs balance, the trial balance proves
// Σdebit = Σcredit, the reconciliation variance equals the one unmatched statement line, and
// the FX rows net to the revaluation entry the workstation prefills.

export const openings = { "1100": 120000 };

// General ledger postings — every ref is a balanced Dr/Cr pair. JE-1072 is a voided duplicate
// (struck through and excluded from totals by LedgerTable). Rows carry a row-level `envelope`:
// settled cash movements are sourced and confirmed (no marks — quiet is the reward for a clean
// figure); the accrual is an estimate, the interest is derived, the void is overridden with a
// reason. Provenance travels with the figure per guidelines/VALUE_ENVELOPE.md.
const PERIOD = { start: "2026-06-01", end: "2026-06-30" };
const settled = (posting, ref) => ({
  condition: "confirmed", authority: "source",
  clocks: { posting, effective: posting, reportingPeriod: PERIOD },
  evidence: [{ label: "Custodian confirm", ref }],
});

export const seedLedger = [
  // sourceBreak/accountId close the loop back to reconciliation (app-map.js › LOOPS.entry):
  // CASE-4181 is the custody cash break whose statement side is this $82,440.00 settlement.
  { date: "06-02", ref: "JE-1042", account: "1100 · Cash", memo: "Client settlement — AAPL block", debit: 82440, credit: "",
    sourceBreak: "CASE-4181", accountId: "1100",
    envelope: settled("2026-06-02", "CONF-88431") },
  { date: "06-02", ref: "JE-1042", account: "1200 · Brokerage receivable", memo: "Client settlement — AAPL block", debit: "", credit: 82440,
    envelope: settled("2026-06-02", "CONF-88431") },
  { date: "06-03", ref: "JE-1043", account: "5100 · Commissions & fees", memo: "Commission & exchange fees", debit: 318.5, credit: "",
    envelope: settled("2026-06-03", "INV-2261") },
  { date: "06-03", ref: "JE-1043", account: "1100 · Cash", memo: "Commission & exchange fees", debit: "", credit: 318.5,
    envelope: settled("2026-06-03", "INV-2261") },
  { date: "06-05", ref: "JE-1051", account: "1500 · Custody account", memo: "Wire to custodian", debit: 40000, credit: "",
    envelope: settled("2026-06-05", "WIRE-40118") },
  { date: "06-05", ref: "JE-1051", account: "1100 · Cash", memo: "Wire to custodian", debit: "", credit: 40000,
    envelope: settled("2026-06-05", "WIRE-40118") },
  { date: "06-09", ref: "JE-1060", account: "1200 · Brokerage receivable", memo: "Dividend receivable — MSFT", debit: 1290, credit: "",
    sourceBreak: "CASE-4184", accountId: "1200",
    envelope: { condition: "estimated", detail: "declared rate × record-date shares", authority: "estimate",
      clocks: { posting: "2026-06-09", effective: "2026-06-09", settlement: "2026-07-10", reportingPeriod: PERIOD },
      evidence: [{ label: "Dividend notice", ref: "DIV-MSFT-0609" }] } },
  { date: "06-09", ref: "JE-1060", account: "4200 · Dividend & interest income", memo: "Dividend receivable — MSFT", debit: "", credit: 1290,
    envelope: { condition: "estimated", detail: "declared rate × record-date shares", authority: "estimate",
      clocks: { posting: "2026-06-09", settlement: "2026-07-10", reportingPeriod: PERIOD } } },
  { date: "06-12", ref: "JE-1071", account: "5300 · Financing cost", memo: "Margin interest — June", debit: 1120, credit: "",
    envelope: { condition: "confirmed", detail: "avg debit balance × 6.15%", authority: "derived",
      clocks: { posting: "2026-06-12", calculatedAt: "2026-06-12 22:10:00Z", reportingPeriod: PERIOD },
      evidence: [{ label: "Interest accrual run", ref: "ACCR-0612" }] } },
  { date: "06-12", ref: "JE-1071", account: "1100 · Cash", memo: "Margin interest — June", debit: "", credit: 1120,
    envelope: { condition: "confirmed", detail: "avg debit balance × 6.15%", authority: "derived",
      clocks: { posting: "2026-06-12", calculatedAt: "2026-06-12 22:10:00Z", reportingPeriod: PERIOD } } },
  { date: "06-13", ref: "JE-1072", account: "5100 · Commissions & fees", memo: "Duplicate of JE-1043 — voided", debit: 318.5, credit: "", status: "void",
    envelope: { condition: "overridden", detail: "voided — duplicate of JE-1043", authority: "desk",
      clocks: { posting: "2026-06-13", reportingPeriod: PERIOD },
      evidence: [{ label: "Void memo", ref: "VM-1072" }] } },
  { date: "06-13", ref: "JE-1072", account: "1100 · Cash", memo: "Duplicate of JE-1043 — voided", debit: "", credit: 318.5, status: "void",
    envelope: { condition: "overridden", detail: "voided — duplicate of JE-1043", authority: "desk",
      clocks: { posting: "2026-06-13", reportingPeriod: PERIOD } } },
  { date: "06-16", ref: "JE-1080", account: "1310 · Equities", memo: "Buy 40 NVDA @ 1,603.00", debit: 64120, credit: "",
    envelope: { condition: "confirmed", authority: "source", materiality: { material: true, threshold: "50,000.00" },
      clocks: { trade: "2026-06-16", settlement: "2026-06-18", posting: "2026-06-16", reportingPeriod: PERIOD },
      evidence: [{ label: "Execution report", ref: "EX-77140" }] } },
  { date: "06-16", ref: "JE-1080", account: "1100 · Cash", memo: "Buy 40 NVDA @ 1,603.00", debit: "", credit: 64120,
    envelope: { condition: "confirmed", authority: "source", materiality: { material: true, threshold: "50,000.00" },
      clocks: { trade: "2026-06-16", settlement: "2026-06-18", posting: "2026-06-16", reportingPeriod: PERIOD } } },
  // Intercompany: June interest on loan ICL-01 to MER-SPV-ALPHA — non-cash accrual (settles
  // 2026-07-05), mirrored by SA-0142 in the SPV's books; the pair backs elimination E-1.
  { date: "06-30", ref: "JE-1078", account: "1200 · Brokerage receivable", memo: "IC loan interest — due from MER-SPV-ALPHA (June)", debit: 12400, credit: "",
    envelope: { condition: "confirmed", detail: "ICL-01 · avg June draw 2,400,000.00 × 6.20% / 12", authority: "derived",
      clocks: { posting: "2026-06-30", effective: "2026-06-30", settlement: "2026-07-05", reportingPeriod: PERIOD },
      evidence: [{ label: "IC interest schedule", ref: "ICL-0630" }] } },
  { date: "06-30", ref: "JE-1078", account: "4200 · Dividend & interest income", memo: "IC loan interest — due from MER-SPV-ALPHA (June)", debit: "", credit: 12400,
    envelope: { condition: "confirmed", detail: "mirrors SA-0142 (MER-SPV-ALPHA)", authority: "derived",
      clocks: { posting: "2026-06-30", effective: "2026-06-30", reportingPeriod: PERIOD } } },
];

// Chart of accounts — leaf balances are signed debit-positive (liability/equity/revenue carry
// negative signs); parents roll up in AccountTree. Cash agrees with the ledger closing balance
// (120,000.00 opening − 23,118.50 net June movement, voids excluded).
export const chartOfAccounts = [
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

// Flatten chartOfAccounts leaves into TrialBalance rows — `balance` signed in normal-side terms
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
export const trialBalanceRows = buildTbRows(chartOfAccounts);

// Statement of operations — Q2 vs Q1 comparison. Totals agree with the chartOfAccounts revenue/expense nets.
export const statementSections = [
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
export const statementTotal = { label: "Net income", values: { q2: 362080, q1: 338860 } };

// Cash reconciliation — account 1100, June. One statement line (INT-0210, +142.18) has no
// ledger counterpart; posting the prefilled accrual entry clears it.
export const reconciliationLeft = [
  { id: 1, date: "06-02", ref: "DTC-88421", memo: "Settlement credit", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "FEE-0093", memo: "Commission", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "WIRE-5510", memo: "Custodian wire", amount: -40000, matched: true },
  { id: 4, date: "06-06", ref: "INT-0210", memo: "Interest accrual", amount: 142.18, matched: false },
  { id: 5, date: "06-12", ref: "MRG-0612", memo: "Margin interest", amount: -1120, matched: true },
  { id: 6, date: "06-16", ref: "EQT-7810", memo: "Equity purchase — NVDA", amount: -64120, matched: true },
];
export const reconciliationRight = [
  { id: 1, date: "06-02", ref: "JE-1042", memo: "Client settlement", amount: 82440, matched: true },
  { id: 2, date: "06-03", ref: "JE-1043", memo: "Commission & fees", amount: -318.5, matched: true },
  { id: 3, date: "06-05", ref: "JE-1051", memo: "Wire to custodian", amount: -40000, matched: true },
  { id: 5, date: "06-12", ref: "JE-1071", memo: "Margin interest", amount: -1120, matched: true },
  { id: 6, date: "06-16", ref: "JE-1080", memo: "Buy 40 NVDA", amount: -64120, matched: true },
];

// Tax lots — proceeds differ slightly from market value (actual sale prints).
export const taxLots = [
  { acquired: "2024-11-03", symbol: "MSFT", quantity: 120, costBasis: 49200, marketValue: 53880, proceeds: 54110 },
  { acquired: "2025-02-14", symbol: "AAPL", quantity: 400, costBasis: 68240, marketValue: 79120, proceeds: 78650 },
  { acquired: "2026-01-09", symbol: "NVDA", quantity: 150, costBasis: 121500, marketValue: 138900, proceeds: 139420 },
  { acquired: "2026-03-17", symbol: "TLT", quantity: 320, costBasis: 28227, marketValue: 29136, proceeds: 29010 },
  { acquired: "2026-04-22", symbol: "TSLA", quantity: 200, costBasis: 49800, marketValue: 44120, proceeds: 43890 },
];

// FX revaluation — rates are base units per 1 local unit. Nets to +1,828.00 USD.
export const fxRows = [
  { currency: "EUR", account: "Prime broker cash", local: 1250000, bookedRate: 1.0842, currentRate: 1.0918 },
  { currency: "GBP", account: "Margin posted — LSE", local: 420000, bookedRate: 1.2704, currentRate: 1.2652 },
  { currency: "JPY", account: "Futures margin — OSE", local: 98000000, bookedRate: 0.006701, currentRate: 0.006645 },
];

// ————— O-5 · The group — entity as a structure, consolidation as a surface —————
// Entity is a PATH in the ownership tree, not a flat value. The tree agrees with the
// ownership-graph template's register; scope pick restates GL and Statements, and group
// scope renders consolidated figures with the elimination layer disclosed — never pre-summed.
export const entityTree = {
  id: "MER-GRP", label: "Meridian Capital Group", type: "group", children: [
    { id: "MER-FUND-I", label: "Meridian Fund I LP", type: "fund", children: [
      { id: "MER-SPV-ALPHA", label: "Meridian SPV Alpha LLC", type: "spv" },
    ]},
    { id: "MER-MGMT", label: "Meridian Management LLC", type: "manager" },
  ],
};
export const entityPaths = [
  { id: "MER-GRP", label: "Meridian Capital Group", path: "Meridian Capital Group — consolidated", depth: 0 },
  { id: "MER-FUND-I", label: "Meridian Fund I LP", path: "Group ▸ Meridian Fund I LP", depth: 1 },
  { id: "MER-SPV-ALPHA", label: "Meridian SPV Alpha LLC", path: "Group ▸ Fund I ▸ Meridian SPV Alpha LLC", depth: 2 },
  { id: "MER-MGMT", label: "Meridian Management LLC", path: "Group ▸ Meridian Management LLC", depth: 1 },
];

// Consolidated statement of operations, Q2 — per-entity columns; each row's elimination is a
// signed amount tied to an elimination id below. The Fund I column agrees exactly with
// statementSections (451,220 revenue · (89,140) expenses · 362,080 NI). The group column is
// derived in the view as Σ entities + elimination, so the arithmetic is visible, never stated.
export const consolidation = {
  period: "2026-Q2", currency: "USD",
  entities: [
    { key: "fund", id: "MER-FUND-I", label: "Fund I" },
    { key: "spv", id: "MER-SPV-ALPHA", label: "SPV Alpha" },
    { key: "mgmt", id: "MER-MGMT", label: "Management" },
  ],
  sections: [
    { label: "Revenue", rows: [
      { label: "Realized trading gains", values: { fund: 412800, spv: 96300, mgmt: 0 } },
      { label: "Dividend & interest income", values: { fund: 38420, spv: 4180, mgmt: 0 }, elim: { amount: -12400, ref: "E-1" } },
      { label: "Advisory & administration fees", values: { fund: 0, spv: 0, mgmt: 148500 }, elim: { amount: -8500, ref: "E-2" } },
    ]},
    { label: "Operating expenses", rows: [
      { label: "Commissions & fees", values: { fund: -28440, spv: -6210, mgmt: 0 } },
      { label: "Data & infrastructure", values: { fund: -19500, spv: 0, mgmt: -22300 } },
      { label: "Financing cost", values: { fund: -41200, spv: -18650, mgmt: 0 }, elim: { amount: 12400, ref: "E-1" } },
      { label: "Administration fee — affiliate", values: { fund: 0, spv: -8500, mgmt: 0 }, elim: { amount: 8500, ref: "E-2" } },
    ]},
  ],
};

// Eliminations — every figure in the eliminations column traces here, and each opens its JE
// pair: two balanced entries, one per entity, that net to zero at group (IntercompanyZero).
export const eliminations = [
  { id: "E-1", label: "Intercompany loan interest", amount: 12400,
    lines: "Dividend & interest income (Fund I) ↔ Financing cost (SPV Alpha)",
    basis: "Revolving IC facility ICL-01 — average June draw 2,400,000.00 at 6.20%, repaid intra-month (no 06-30 principal) — June interest accrued, settles 2026-07-05.",
    mirror: { a: { entity: "Fund I", account: "1200 · Brokerage receivable", amount: 12400 },
              b: { entity: "SPV Alpha", account: "2400 · Payable to affiliate", amount: -12400 } },
    jes: [
      { entityId: "MER-FUND-I", entity: "Meridian Fund I LP", ref: "JE-1078", date: "2026-06-30",
        memo: "IC loan interest — due from MER-SPV-ALPHA (June)",
        lines: [{ account: "1200 · Brokerage receivable", debit: 12400 }, { account: "4200 · Dividend & interest income", credit: 12400 }] },
      { entityId: "MER-SPV-ALPHA", entity: "Meridian SPV Alpha LLC", ref: "SA-0142", date: "2026-06-30",
        memo: "IC loan interest — due to MER-FUND-I (June)",
        lines: [{ account: "5300 · Financing cost", debit: 12400 }, { account: "2400 · Payable to affiliate", credit: 12400 }] },
    ]},
  { id: "E-2", label: "Administration fee — affiliate", amount: 8500,
    lines: "Advisory & administration fees (Management) ↔ Administration fee — affiliate (SPV Alpha)",
    basis: "Q2 fund-administration fee under the MER-MGMT / MER-SPV-ALPHA services agreement, billed 2026-06-30.",
    mirror: { a: { entity: "Management", account: "1250 · Receivable from affiliate", amount: 8500 },
              b: { entity: "SPV Alpha", account: "2400 · Payable to affiliate", amount: -8500 } },
    jes: [
      { entityId: "MER-MGMT", entity: "Meridian Management LLC", ref: "MG-0288", date: "2026-06-30",
        memo: "Q2 administration fee — MER-SPV-ALPHA",
        lines: [{ account: "1250 · Receivable from affiliate", debit: 8500 }, { account: "4600 · Administration fee revenue", credit: 8500 }] },
      { entityId: "MER-SPV-ALPHA", entity: "Meridian SPV Alpha LLC", ref: "SA-0147", date: "2026-06-30",
        memo: "Q2 administration fee — due to MER-MGMT",
        lines: [{ account: "5600 · Fund administration — affiliate", debit: 8500 }, { account: "2400 · Payable to affiliate", credit: 8500 }] },
    ]},
];

// Affiliate ledgers — GL restates by scope: fund shows seedLedger, an affiliate shows its own
// book, group shows all three with entity-tagged memos (disclosed, not silently merged).
// Every elimination JE ref above is present in its entity's ledger.
export const icLedgers = {
  "MER-SPV-ALPHA": [
    { date: "06-11", ref: "SA-0131", account: "1310 · Equities", memo: "Buy 500 XLE @ 61.00", debit: 30500, credit: "" },
    { date: "06-11", ref: "SA-0131", account: "1100 · Cash", memo: "Buy 500 XLE @ 61.00", debit: "", credit: 30500 },
    { date: "06-30", ref: "SA-0142", account: "5300 · Financing cost", memo: "IC loan interest — due to MER-FUND-I (June)", debit: 12400, credit: "",
      envelope: { condition: "confirmed", detail: "mirrors JE-1078 (MER-FUND-I)", authority: "derived",
        clocks: { posting: "2026-06-30", reportingPeriod: PERIOD }, evidence: [{ label: "IC interest schedule", ref: "ICL-0630" }] } },
    { date: "06-30", ref: "SA-0142", account: "2400 · Payable to affiliate", memo: "IC loan interest — due to MER-FUND-I (June)", debit: "", credit: 12400 },
    { date: "06-30", ref: "SA-0147", account: "5600 · Fund administration — affiliate", memo: "Q2 administration fee — due to MER-MGMT", debit: 8500, credit: "" },
    { date: "06-30", ref: "SA-0147", account: "2400 · Payable to affiliate", memo: "Q2 administration fee — due to MER-MGMT", debit: "", credit: 8500 },
  ],
  "MER-MGMT": [
    { date: "06-16", ref: "MG-0270", account: "1100 · Cash", memo: "Advisory fee — external mandate ARC-4", debit: 49500, credit: "" },
    { date: "06-16", ref: "MG-0270", account: "4500 · Advisory fee revenue", memo: "Advisory fee — external mandate ARC-4", debit: "", credit: 49500 },
    { date: "06-30", ref: "MG-0288", account: "1250 · Receivable from affiliate", memo: "Q2 administration fee — MER-SPV-ALPHA", debit: 8500, credit: "",
      envelope: { condition: "confirmed", detail: "mirrors SA-0147 (MER-SPV-ALPHA)", authority: "derived",
        clocks: { posting: "2026-06-30", reportingPeriod: PERIOD } } },
    { date: "06-30", ref: "MG-0288", account: "4600 · Administration fee revenue", memo: "Q2 administration fee — MER-SPV-ALPHA", debit: "", credit: 8500 },
  ],
};

// Single-entity statement of operations derived from the consolidation dataset (Q2 only —
// affiliates carry no Q1 comparative in this seed). Zero lines are dropped.
export function entityStatement(id) {
  const ent = consolidation.entities.find((e) => e.id === id);
  if (!ent) return { sections: [], total: { label: "Net income", values: { q2: 0 } } };
  let ni = 0;
  const sections = consolidation.sections.map((sec) => {
    const rows = sec.rows.filter((r) => (r.values[ent.key] || 0) !== 0)
      .map((r) => ({ label: r.label, values: { q2: r.values[ent.key] } }));
    const sub = rows.reduce((a, r) => a + r.values.q2, 0);
    ni += sub;
    return { label: sec.label, rows, subtotal: { label: "Total " + sec.label.toLowerCase(), values: { q2: sub } } };
  }).filter((sec) => sec.rows.length);
  return { sections, total: { label: "Net income", values: { q2: ni } } };
}

// Per-entity trial balances — normal-side signed positive, each proves Σdebit = Σcredit
// (SPV: D 649,660.00 · MGMT: D 201,100.00). Revenue/expense lines agree with the entity's
// consolidation column; 2400/1250 carry the IC mirror balances behind E-1 · E-2.
export const entityTb = {
  "MER-SPV-ALPHA": [
    { code: "1100", account: "Cash & equivalents", type: "asset", balance: 219500 },
    { code: "1310", account: "Equities", type: "asset", balance: 396800 },
    { code: "2400", account: "Payable to affiliate", type: "liability", balance: 20900 },
    { code: "3100", account: "Contributed capital", type: "equity", balance: 528280 },
    { code: "4100", account: "Realized trading gains", type: "revenue", balance: 96300 },
    { code: "4200", account: "Dividend & interest income", type: "revenue", balance: 4180 },
    { code: "5100", account: "Commissions & fees", type: "expense", balance: 6210 },
    { code: "5300", account: "Financing cost", type: "expense", balance: 18650 },
    { code: "5600", account: "Fund administration — affiliate", type: "expense", balance: 8500 },
  ],
  "MER-MGMT": [
    { code: "1100", account: "Cash & equivalents", type: "asset", balance: 134300 },
    { code: "1250", account: "Receivable from affiliate", type: "asset", balance: 8500 },
    { code: "1600", account: "Equipment & software", type: "asset", balance: 36000 },
    { code: "3100", account: "Contributed capital", type: "equity", balance: 52600 },
    { code: "4500", account: "Advisory fee revenue", type: "revenue", balance: 140000 },
    { code: "4600", account: "Administration fee revenue", type: "revenue", balance: 8500 },
    { code: "5200", account: "Data & infrastructure", type: "expense", balance: 22300 },
  ],
};

// Per-entity cash reconciliations — both affiliates fully matched (the only open break in
// the group is Fund I's INT-0210). Group scope concatenates all three, entity-tagged.
export const entityRecons = {
  "MER-SPV-ALPHA": {
    left: [{ id: 1, date: "06-11", ref: "EQT-3110", memo: "Equity purchase — XLE", amount: -30500, matched: true }],
    right: [{ id: 1, date: "06-11", ref: "SA-0131", memo: "Buy 500 XLE", amount: -30500, matched: true }],
  },
  "MER-MGMT": {
    left: [{ id: 1, date: "06-16", ref: "ACH-2216", memo: "Advisory fee receipt — ARC-4", amount: 49500, matched: true }],
    right: [{ id: 1, date: "06-16", ref: "MG-0270", memo: "Advisory fee — external mandate ARC-4", amount: 49500, matched: true }],
  },
};

// Per-entity close state — both affiliates locked their 2026-Q2 books; the group close
// derives from entity locks (computed in the workstation, never stated).
export const entityClose = {
  "MER-SPV-ALPHA": {
    lockedAt: "2026-06-30 18:02:11Z", postings: 6,
    gates: [
      { key: "post", label: "Journal postings", status: "Passed" },
      { key: "recon", label: "Cash reconciliation", status: "Passed" },
      { key: "fx", label: "FX revaluation", status: "Passed" },
      { key: "tb", label: "Trial balance proof", status: "Passed" },
      { key: "lock", label: "Period lock", status: "Passed" },
    ],
    issues: [{ code: "CLOSE-LOCKED", severity: "Info", message: "Locked 2026-06-30 18:02:11Z — postings disabled. USD book — FX gate passed with no reval required." }],
  },
  "MER-MGMT": {
    lockedAt: "2026-06-30 17:44:57Z", postings: 4,
    gates: [
      { key: "post", label: "Journal postings", status: "Passed" },
      { key: "recon", label: "Cash reconciliation", status: "Passed" },
      { key: "fx", label: "FX revaluation", status: "Passed" },
      { key: "tb", label: "Trial balance proof", status: "Passed" },
      { key: "lock", label: "Period lock", status: "Passed" },
    ],
    issues: [{ code: "CLOSE-LOCKED", severity: "Info", message: "Locked 2026-06-30 17:44:57Z — postings disabled. USD book — FX gate passed with no reval required." }],
  },
};

// Per-entity charts of accounts — signed like chartOfAccounts (credit-normal negative);
// leaves agree with entityTb, so each entity's tree also nets to 0.00 (double-entry proof).
export const entityCoa = {
  "MER-SPV-ALPHA": [
    { code: "1000", name: "Assets", type: "asset", children: [
      { code: "1100", name: "Cash & equivalents", type: "asset", balance: 219500 },
      { code: "1310", name: "Equities", type: "asset", balance: 396800 },
    ]},
    { code: "2000", name: "Liabilities", type: "liability", children: [
      { code: "2400", name: "Payable to affiliate", type: "liability", balance: -20900 },
    ]},
    { code: "3000", name: "Equity", type: "equity", children: [
      { code: "3100", name: "Contributed capital", type: "equity", balance: -528280 },
    ]},
    { code: "4000", name: "Revenue", type: "revenue", children: [
      { code: "4100", name: "Realized trading gains", type: "revenue", balance: -96300 },
      { code: "4200", name: "Dividend & interest income", type: "revenue", balance: -4180 },
    ]},
    { code: "5000", name: "Expenses", type: "expense", children: [
      { code: "5100", name: "Commissions & fees", type: "expense", balance: 6210 },
      { code: "5300", name: "Financing cost", type: "expense", balance: 18650 },
      { code: "5600", name: "Fund administration — affiliate", type: "expense", balance: 8500 },
    ]},
  ],
  "MER-MGMT": [
    { code: "1000", name: "Assets", type: "asset", children: [
      { code: "1100", name: "Cash & equivalents", type: "asset", balance: 134300 },
      { code: "1250", name: "Receivable from affiliate", type: "asset", balance: 8500 },
      { code: "1600", name: "Equipment & software", type: "asset", balance: 36000 },
    ]},
    { code: "3000", name: "Equity", type: "equity", children: [
      { code: "3100", name: "Contributed capital", type: "equity", balance: -52600 },
    ]},
    { code: "4000", name: "Revenue", type: "revenue", children: [
      { code: "4500", name: "Advisory fee revenue", type: "revenue", balance: -140000 },
      { code: "4600", name: "Administration fee revenue", type: "revenue", balance: -8500 },
    ]},
    { code: "5000", name: "Expenses", type: "expense", children: [
      { code: "5200", name: "Data & infrastructure", type: "expense", balance: 22300 },
    ]},
  ],
};

// Group chart — the five standard roots, each holding per-entity sub-groups (gross, tagged).
// Leaf codes get an entity prefix so tree selection stays unique; `baseCode` + `entity`
// preserve exact ledger matching in the account inspector.
export function groupCoa() {
  const ents = [["F", "MER-FUND-I", chartOfAccounts], ["S", "MER-SPV-ALPHA", entityCoa["MER-SPV-ALPHA"]], ["M", "MER-MGMT", entityCoa["MER-MGMT"]]];
  const tag = (nodes, ent, px) => nodes.map((n) => {
    const t = { ...n, code: px + "-" + n.code, baseCode: n.code, entity: ent };
    if (n.children) t.children = tag(n.children, ent, px);
    return t;
  });
  return [["1000", "Assets", "asset"], ["2000", "Liabilities", "liability"], ["3000", "Equity", "equity"], ["4000", "Revenue", "revenue"], ["5000", "Expenses", "expense"]]
    .map(([code, name, type]) => ({
      code, name, type,
      children: ents.filter(([, , coa]) => coa.some((r) => r.code === code))
        .map(([px, ent, coa]) => ({ code: px + "-" + code, name: ent, type, entity: ent,
          children: tag(coa.find((r) => r.code === code).children, ent, px) })),
    }));
}

// Per-entity tax lots — SPV holds one lot; Management runs no trading book.
export const entityLots = {
  "MER-SPV-ALPHA": [
    { acquired: "2026-06-11", symbol: "XLE", quantity: 500, costBasis: 30500, marketValue: 31150, proceeds: 31090 },
  ],
  "MER-MGMT": [],
};

export const accountOptions = [
  "1100 · Cash", "1200 · Brokerage receivable", "1310 · Equities",
  "1400 · FX translation adjustment", "1500 · Custody account",
  "2100 · Margin payable", "2200 · Accrued fees",
  "4100 · Realized trading gains", "4200 · Dividend & interest income",
  "4300 · Unrealized FX gain/loss",
  "5100 · Commissions & fees", "5200 · Data & infrastructure", "5300 · Financing cost",
];
