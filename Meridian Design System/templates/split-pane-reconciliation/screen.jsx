// Split Pane Reconciliation — template screen. Mounted by the DC via <x-import>.
// Reads design-system components from the compiled bundle (window.MeridianDesignSystem_4f61be)
// and the single global React. Functional: two synchronized panes show custodian statement vs.
// internal ledger; clicking a row cross-highlights its match on the other side; unmatched rows
// open a break resolution drawer.
const {
  WorkstationTopbar, NavRail, StatusBar, Eyebrow, Button, Badge,
  MetricCard, AmountCell, Select
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo, useCallback, useRef, useEffect } = React;

/* ── screen-level CSS ── */
(function injectCss() {
  if (document.getElementById("spr-screen-css")) return;
  const el = document.createElement("style");
  el.id = "spr-screen-css";
  el.textContent = `
.spr-controls{display:flex;align-items:center;gap:8px;flex-wrap:wrap;}
.spr-controls__label{font-family:var(--font-body);font-size:11px;color:var(--text-muted);
  font-variant:all-small-caps;font-weight:600;letter-spacing:.03em;white-space:nowrap;}
.spr-controls__sel{width:160px;}
.spr-seg{display:inline-flex;border:1px solid var(--border);border-radius:var(--radius-button,6px);overflow:hidden;}
.spr-seg button{appearance:none;border:none;background:var(--bg-light);cursor:pointer;
  font-family:var(--font-body);font-size:12px;color:var(--text-secondary);
  padding:6px 12px;border-right:1px solid var(--border);transition:all .12s;}
.spr-seg button:last-child{border-right:none;}
.spr-seg button:hover{background:var(--bg-hover);color:var(--text-primary);}
.spr-seg button[aria-pressed="true"]{background:var(--bg-active,#E6EEF5);color:var(--accent);font-weight:600;box-shadow:inset 0 -2px 0 var(--accent);}
.spr-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;}
.spr-split{display:grid;grid-template-columns:1fr 1fr;gap:0;border:1px solid var(--border);
  border-radius:var(--radius-card,8px);overflow:hidden;background:var(--bg-light);
  box-shadow:var(--shadow-card,0 1px 1px rgba(0,0,0,.08));flex:1;min-height:0;}
.spr-pane{display:flex;flex-direction:column;min-width:0;overflow:hidden;}
.spr-pane+.spr-pane{border-left:1px solid var(--border);}
.spr-pane__head{display:flex;align-items:center;justify-content:space-between;gap:8px;
  padding:9px 13px;background:var(--bg-medium,#F5F7FA);border-bottom:1px solid var(--border);
  flex-shrink:0;}
.spr-pane__title{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted);}
.spr-pane__chips{display:flex;gap:5px;}
.spr-chip{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.02em;border:1px solid;border-radius:var(--radius-chip,4px);
  padding:1px 7px;line-height:1.5;white-space:nowrap;}
.spr-chip--ok{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#126C4D);}
.spr-chip--break{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#983244);}
.spr-chip--timing{background:var(--orange-a10,rgba(183,121,31,.10));border-color:var(--orange,#B7791F);color:var(--orange-dim,#946216);}
.spr-pane__scroll{overflow-y:auto;flex:1;min-height:0;}
.spr-table{width:100%;border-collapse:collapse;}
.spr-table th{position:sticky;top:0;z-index:1;background:var(--bg-light);
  font-family:var(--font-body);font-size:9px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.04em;color:var(--text-muted);padding:6px 10px;
  border-bottom:1px solid var(--border);white-space:nowrap;text-align:left;}
.spr-table th.num{text-align:right;}
.spr-table td{font-family:var(--font-body);font-size:12px;color:var(--text-primary);
  padding:6px 10px;border-top:1px solid var(--border);vertical-align:middle;white-space:nowrap;}
.spr-table td.mono{font-family:var(--font-data);font-size:11px;color:var(--text-muted);}
.spr-table td.memo{white-space:normal;max-width:0;width:99%;}
.spr-table td.num{text-align:right;}
.spr-table tbody tr{cursor:pointer;position:relative;}
.spr-table tbody tr td:first-child{padding-left:13px;position:relative;}
.spr-table tbody tr td:first-child::before{content:"";position:absolute;left:0;top:0;
  bottom:0;width:3px;border-radius:0 2px 2px 0;}
.spr-table tr.matched td:first-child::before{background:var(--green,#16885F);}
.spr-table tr.timing td:first-child::before{background:var(--orange,#B7791F);}
.spr-table tr.break td:first-child::before{background:var(--red,#BA3F55);}
.spr-table tr.timing td{background:rgba(183,121,31,.05);}
.spr-table tr.break td{background:var(--red-a10,rgba(186,63,85,.07));}
.spr-table tbody tr:hover td{background:var(--bg-hover,#F1F4F7);}
.spr-table tr.break:hover td,.spr-table tr.timing:hover td{filter:brightness(.97);}
.spr-table tr.selected td{background:var(--blue-a10,rgba(47,111,143,.10))!important;}
.spr-table tr.cross-lit td{background:var(--blue-a10,rgba(47,111,143,.06))!important;
  outline:1px dashed var(--accent);outline-offset:-1px;}
.spr-table tr.selected td:first-child::before,
.spr-table tr.cross-lit td:first-child::before{background:var(--accent,#2F6F8F)!important;}
.spr-status-dot{display:inline-block;width:7px;height:7px;border-radius:50%;flex-shrink:0;}
.spr-empty{padding:24px 16px;text-align:center;font-family:var(--font-body);font-size:12px;color:var(--text-muted);}
.spr-summary{display:grid;grid-template-columns:1fr 1fr auto;gap:0;
  border-top:2px solid var(--border-strong,#AAB4BF);background:var(--bg-medium,#F5F7FA);}
.spr-sum-cell{padding:10px 14px;display:flex;flex-direction:column;gap:3px;}
.spr-sum-cell+.spr-sum-cell{border-left:1px solid var(--border);}
.spr-sum-label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted);}
.spr-status-badge{display:flex;align-items:center;gap:7px;
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;border-left:1px solid var(--border);
  padding:10px 16px;white-space:nowrap;}
.spr-status-badge--rec{color:var(--green-dim,#126C4D);}
.spr-status-badge--out{color:var(--red-dim,#983244);}
.spr-status-dot-lg{width:8px;height:8px;border-radius:50%;background:currentColor;flex-shrink:0;}
.spr-break-overlay{position:fixed;inset:0;background:rgba(23,26,31,.32);z-index:50;
  display:flex;justify-content:flex-end;animation:spr-fade .14s ease;}
@keyframes spr-fade{from{opacity:0;}}
.spr-break-drawer{width:460px;max-width:92vw;height:100%;background:var(--bg);
  display:flex;flex-direction:column;box-shadow:-12px 0 40px rgba(23,26,31,.2);
  animation:spr-slide .18s cubic-bezier(.2,.7,.3,1);}
@keyframes spr-slide{from{transform:translateX(24px);opacity:.6;}}
.spr-break-drawer__hd{display:flex;align-items:flex-start;justify-content:space-between;
  padding:16px 18px;border-bottom:1px solid var(--border);background:var(--bg-light);gap:12px;}
.spr-break-drawer__body{flex:1;overflow-y:auto;padding:18px;}
.spr-break-drawer__ft{padding:14px 18px;border-top:1px solid var(--border);
  background:var(--bg-light);display:flex;gap:8px;flex-wrap:wrap;}
.spr-x-btn{appearance:none;border:1px solid var(--border);background:var(--bg-light);
  width:28px;height:28px;border-radius:var(--radius-button,6px);cursor:pointer;
  color:var(--text-muted);font-size:16px;line-height:1;flex-shrink:0;
  display:inline-flex;align-items:center;justify-content:center;}
.spr-x-btn:hover{background:var(--bg-hover);color:var(--text-secondary);}
.spr-kv{display:grid;grid-template-columns:140px 1fr;gap:6px 12px;align-items:baseline;}
.spr-kv-key{font-family:var(--font-body);font-size:11px;color:var(--text-muted);font-weight:500;}
.spr-kv-val{font-family:var(--font-data);font-size:12px;color:var(--text-primary);}
.spr-divider{height:1px;background:var(--border);margin:14px 0;}
.spr-near-match{border:1px solid var(--border);border-radius:var(--radius-card,8px);
  padding:10px 12px;background:var(--bg-light);display:flex;flex-direction:column;gap:4px;}
.spr-near-match__row{display:flex;justify-content:space-between;align-items:baseline;gap:8px;}
.spr-near-match__label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted);}
.spr-near-match__val{font-family:var(--font-data);font-size:12px;color:var(--text-primary);}
.spr-near-match__diff{font-family:var(--font-data);font-size:11px;color:var(--orange,#B7791F);}
.spr-cat-badge{display:inline-block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.02em;color:var(--text-muted);
  background:var(--bg-medium,#F5F7FA);border:1px solid var(--border);
  border-radius:var(--radius-chip,4px);padding:1px 6px;}
`;
  document.head.appendChild(el);
})();

/* ── Seed data ── */
const PERIODS = ["Jun 2026", "May 2026", "Apr 2026", "Q2 2026"];
const ACCOUNTS = ["All accounts", "1100 · Cash", "1500 · Custody"];

// Custodian / prime-broker statement items
const STMT_SEED = [
  { id:"S01", date:"06-02", ref:"DTC-88421", memo:"Settlement credit — AAPL block",     category:"Settlement", amount:82440,   matchId:"L01", matchStatus:"matched"  },
  { id:"S02", date:"06-03", ref:"FEE-0093",  memo:"Commission & exchange fees",           category:"Fee",        amount:-318.5,  matchId:"L02", matchStatus:"matched"  },
  { id:"S03", date:"06-05", ref:"WIRE-5510", memo:"Wire transfer to custodian",           category:"Transfer",   amount:-40000,  matchId:"L03", matchStatus:"matched"  },
  { id:"S04", date:"06-06", ref:"INT-0210",  memo:"Interest accrual on margin balance",   category:"Interest",   amount:142.18,  matchId:null,  matchStatus:"break"    },
  { id:"S05", date:"06-09", ref:"DIV-7741",  memo:"Dividend credit — MSFT",              category:"Dividend",   amount:1290,    matchId:"L05", matchStatus:"matched"  },
  { id:"S06", date:"06-11", ref:"DTC-88580", memo:"Settlement credit — TSLA short cover",category:"Settlement", amount:24180,   matchId:"L06", matchStatus:"matched"  },
  { id:"S07", date:"06-14", ref:"FEE-0094",  memo:"Annual custody charge",               category:"Fee",        amount:-850,    matchId:"L07", matchStatus:"timing"   },
  { id:"S08", date:"06-17", ref:"DIV-7788",  memo:"Dividend credit — TLT ETF",           category:"Dividend",   amount:346.5,   matchId:"L08", matchStatus:"matched"  },
  { id:"S09", date:"06-20", ref:"CORP-3301", memo:"Corporate action — NVDA 10:1 split",  category:"Corporate",  amount:0,       matchId:null,  matchStatus:"break"    },
  { id:"S10", date:"06-24", ref:"WIRE-5598", memo:"Wire receipt from custodian",          category:"Transfer",   amount:15000,   matchId:"L10", matchStatus:"matched"  },
];

// Internal ledger items
const LDGR_SEED = [
  { id:"L01", date:"06-02", ref:"JE-1042",   memo:"Client settlement — AAPL block",      category:"Settlement", amount:82440,   matchId:"S01", matchStatus:"matched"  },
  { id:"L02", date:"06-03", ref:"JE-1043",   memo:"Commission & exchange fees",           category:"Fee",        amount:-318.5,  matchId:"S02", matchStatus:"matched"  },
  { id:"L03", date:"06-05", ref:"JE-1051",   memo:"Wire to custodian",                   category:"Transfer",   amount:-40000,  matchId:"S03", matchStatus:"matched"  },
  { id:"L05", date:"06-09", ref:"JE-1060",   memo:"Dividend receivable — MSFT",          category:"Dividend",   amount:1290,    matchId:"S05", matchStatus:"matched"  },
  { id:"L06", date:"06-11", ref:"JE-1072",   memo:"TSLA short settlement",               category:"Settlement", amount:24180,   matchId:"S06", matchStatus:"matched"  },
  { id:"L07", date:"06-16", ref:"JE-1078",   memo:"Custody fee accrual",                 category:"Fee",        amount:-850,    matchId:"S07", matchStatus:"timing"   },
  { id:"L08", date:"06-17", ref:"JE-1081",   memo:"Dividend income — TLT ETF",           category:"Dividend",   amount:346.5,   matchId:"S08", matchStatus:"matched"  },
  { id:"L10", date:"06-24", ref:"JE-1095",   memo:"Wire receipt from custodian",          category:"Transfer",   amount:15000,   matchId:"S10", matchStatus:"matched"  },
  { id:"L11", date:"06-24", ref:"JE-1096",   memo:"Management fee payable Q2",           category:"Fee",        amount:-2500,   matchId:null,  matchStatus:"break"    },
];

const STATUS_DOT_COLOR = {
  matched: "var(--green,#16885F)",
  timing:  "var(--orange,#B7791F)",
  break:   "var(--red,#BA3F55)",
};

const STATUS_LABEL = {
  matched: "Matched",
  timing:  "Timing break",
  break:   "Break",
};

/* ── sub-components ── */
function Pane({ title, items, matchedCount, breakCount, timingCount, selectedId, crossId, onSelect, currency }) {
  return (
    <div className="spr-pane">
      <div className="spr-pane__head">
        <span className="spr-pane__title">{title}</span>
        <span className="spr-pane__chips">
          <span className="spr-chip spr-chip--ok">{matchedCount} matched</span>
          {timingCount > 0 && <span className="spr-chip spr-chip--timing">{timingCount} timing</span>}
          {breakCount > 0 && <span className="spr-chip spr-chip--break">{breakCount} break{breakCount > 1 ? "s" : ""}</span>}
        </span>
      </div>
      <div className="spr-pane__scroll">
        <table className="spr-table">
          <thead>
            <tr>
              <th>Date</th>
              <th>Reference</th>
              <th className="memo">Memo</th>
              <th>Category</th>
              <th className="num">Amount</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 && (
              <tr><td colSpan={5} className="spr-empty">No items to display</td></tr>
            )}
            {items.map((it) => {
              const isSel = selectedId === it.id;
              const isCross = crossId === it.id;
              const cls = [
                it.matchStatus,
                isSel   ? "selected"  : "",
                isCross ? "cross-lit" : "",
              ].filter(Boolean).join(" ");
              return (
                <tr key={it.id} className={cls} onClick={() => onSelect(it)}>
                  <td className="mono">{it.date}</td>
                  <td className="mono">{it.ref}</td>
                  <td className="memo">{it.memo}</td>
                  <td><span className="spr-cat-badge">{it.category}</span></td>
                  <td className="num">
                    <AmountCell value={it.amount} currency={currency} parens />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function BreakDrawer({ item, counterItem, side, onClose, onResolve }) {
  const isTimingBreak = item.matchStatus === "timing";
  const isHardBreak   = item.matchStatus === "break";
  const diffAmt = counterItem ? Math.abs(item.amount - counterItem.amount) : null;
  const diffDays = counterItem && isTimingBreak ? Math.abs(
    new Date("2026-" + item.date.replace("-", "-")).getDate() -
    new Date("2026-" + counterItem.date.replace("-", "-")).getDate()
  ) : null;

  return (
    <div className="spr-break-overlay" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="spr-break-drawer" role="dialog" aria-label="Break details">
        <div className="spr-break-drawer__hd">
          <div style={{ display:"flex", flexDirection:"column", gap:3 }}>
            <Eyebrow>{isTimingBreak ? "Timing Break" : isHardBreak ? "Unmatched Item" : "Reconciliation Break"}</Eyebrow>
            <span style={{ font:"600 15px var(--font-display)", color:"var(--text-primary)" }}>
              {item.ref} · {item.memo.length > 46 ? item.memo.slice(0, 43) + "…" : item.memo}
            </span>
            <span style={{ fontFamily:"var(--font-body)", fontSize:11, color:"var(--text-muted)", marginTop:1 }}>
              {side === "stmt" ? "Custodian statement" : "Internal ledger"} · {item.date}
            </span>
          </div>
          <button className="spr-x-btn" aria-label="Close" onClick={onClose}>×</button>
        </div>

        <div className="spr-break-drawer__body">
          <Eyebrow>Item details</Eyebrow>
          <div style={{ marginTop:10 }} className="spr-kv">
            <span className="spr-kv-key">Reference</span>
            <span className="spr-kv-val">{item.ref}</span>
            <span className="spr-kv-key">Date</span>
            <span className="spr-kv-val">2026-{item.date}</span>
            <span className="spr-kv-key">Memo</span>
            <span className="spr-kv-val">{item.memo}</span>
            <span className="spr-kv-key">Category</span>
            <span className="spr-kv-val"><span className="spr-cat-badge">{item.category}</span></span>
            <span className="spr-kv-key">Amount</span>
            <span className="spr-kv-val">
              <AmountCell value={item.amount} currency="USD" parens strong />
            </span>
            <span className="spr-kv-key">Source</span>
            <span className="spr-kv-val">{side === "stmt" ? "Custodian (DTC / prime)" : "Fund accountant ledger"}</span>
            <span className="spr-kv-key">Match status</span>
            <span className="spr-kv-val" style={{ display:"flex", alignItems:"center", gap:6 }}>
              <span className="spr-status-dot" style={{ background: STATUS_DOT_COLOR[item.matchStatus] }}></span>
              {STATUS_LABEL[item.matchStatus]}
            </span>
          </div>

          {counterItem && (
            <>
              <div className="spr-divider" />
              <Eyebrow>{isTimingBreak ? "Timing match candidate" : "Near-match candidate"}</Eyebrow>
              <div style={{ marginTop:10 }}>
                <div className="spr-near-match">
                  <div className="spr-near-match__row">
                    <span className="spr-near-match__label">{side === "stmt" ? "Ledger entry" : "Statement entry"}</span>
                    <span className="spr-near-match__val">{counterItem.ref}</span>
                  </div>
                  <div className="spr-near-match__row">
                    <span className="spr-near-match__label">Memo</span>
                    <span className="spr-near-match__val" style={{ fontSize:11 }}>{counterItem.memo}</span>
                  </div>
                  <div className="spr-near-match__row">
                    <span className="spr-near-match__label">Date</span>
                    <span className="spr-near-match__val">2026-{counterItem.date}
                      {diffDays != null && diffDays > 0 &&
                        <span className="spr-near-match__diff"> (+{diffDays}d vs statement)</span>}
                    </span>
                  </div>
                  <div className="spr-near-match__row">
                    <span className="spr-near-match__label">Amount</span>
                    <span className="spr-near-match__val">
                      <AmountCell value={counterItem.amount} currency="USD" parens />
                      {diffAmt != null && diffAmt > 0 &&
                        <span className="spr-near-match__diff"> (Δ <AmountCell value={diffAmt} currency="USD" />)</span>}
                    </span>
                  </div>
                </div>
              </div>
            </>
          )}

          {isHardBreak && !counterItem && (
            <>
              <div className="spr-divider" />
              <Eyebrow>No match found</Eyebrow>
              <div style={{ marginTop:10, fontFamily:"var(--font-body)", fontSize:12, color:"var(--text-muted)", lineHeight:1.6 }}>
                This item has no corresponding entry on the{" "}
                {side === "stmt" ? "ledger" : "custodian statement"} side.
                Common causes: timing difference beyond the period window, misclassified transaction,
                or missing journal entry.
              </div>
            </>
          )}

          <div className="spr-divider" />
          <Eyebrow>Resolution options</Eyebrow>
          <div style={{ marginTop:10, display:"flex", flexDirection:"column", gap:8, fontFamily:"var(--font-body)", fontSize:12, color:"var(--text-secondary)" }}>
            {isTimingBreak && (
              <div style={{ padding:"10px 12px", border:"1px solid var(--border)", borderRadius:6, background:"var(--bg-light)" }}>
                <strong style={{ color:"var(--text-primary)", display:"block", marginBottom:4 }}>Accept timing difference</strong>
                Mark as a known accrual timing difference and accept as reconciled for this period.
              </div>
            )}
            {isHardBreak && (
              <div style={{ padding:"10px 12px", border:"1px solid var(--border)", borderRadius:6, background:"var(--bg-light)" }}>
                <strong style={{ color:"var(--text-primary)", display:"block", marginBottom:4 }}>Create adjustment entry</strong>
                Post a journal entry to record the item in the ledger and clear the break.
              </div>
            )}
            <div style={{ padding:"10px 12px", border:"1px solid var(--border)", borderRadius:6, background:"var(--bg-light)" }}>
              <strong style={{ color:"var(--text-primary)", display:"block", marginBottom:4 }}>Mark as permanent break</strong>
              Record as a known reconciliation exception with commentary for the audit trail.
            </div>
          </div>
        </div>

        <div className="spr-break-drawer__ft">
          {isTimingBreak && (
            <Button variant="primary" size="sm" onClick={() => onResolve("accept-timing")}>
              Accept timing match
            </Button>
          )}
          {isHardBreak && (
            <Button variant="primary" size="sm" onClick={() => onResolve("create-je")}>
              Create journal entry
            </Button>
          )}
          <Button variant="ghost" size="sm" onClick={() => onResolve("mark-exception")}>
            Mark exception
          </Button>
          <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
        </div>
      </div>
    </div>
  );
}

/* ── main screen ── */
function SplitPaneReconciliationScreen() {
  const [period,  setPeriod]  = useState(PERIODS[0]);
  const [account, setAccount] = useState(ACCOUNTS[0]);
  const [filter,  setFilter]  = useState("all");
  const [stmtItems, setStmtItems] = useState(STMT_SEED);
  const [ldgrItems, setLdgrItems] = useState(LDGR_SEED);
  const [selectedStmtId, setSelectedStmtId] = useState(null);
  const [selectedLdgrId, setSelectedLdgrId] = useState(null);
  const [breakDrawer, setBreakDrawer] = useState(null); // { item, counterItem, side }

  const applyFilter = useCallback((items) => {
    if (filter === "matched") return items.filter(i => i.matchStatus === "matched");
    if (filter === "open")    return items.filter(i => i.matchStatus !== "matched");
    return items;
  }, [filter]);

  const filteredStmt = useMemo(() => applyFilter(stmtItems), [stmtItems, applyFilter]);
  const filteredLdgr = useMemo(() => applyFilter(ldgrItems), [ldgrItems, applyFilter]);

  // Which ledger item is cross-lit when a statement row is selected
  const crossLitLdgrId = useMemo(() => {
    if (!selectedStmtId) return null;
    const item = stmtItems.find(i => i.id === selectedStmtId);
    return item?.matchId || null;
  }, [selectedStmtId, stmtItems]);

  // Which statement item is cross-lit when a ledger row is selected
  const crossLitStmtId = useMemo(() => {
    if (!selectedLdgrId) return null;
    const item = ldgrItems.find(i => i.id === selectedLdgrId);
    return item?.matchId || null;
  }, [selectedLdgrId, ldgrItems]);

  const handleStmtSelect = useCallback((item) => {
    setSelectedStmtId(prev => prev === item.id ? null : item.id);
    setSelectedLdgrId(null);
    if (item.matchStatus !== "matched") {
      const counter = item.matchId ? ldgrItems.find(l => l.id === item.matchId) : null;
      setBreakDrawer({ item, counterItem: counter, side: "stmt" });
    } else {
      setBreakDrawer(null);
    }
  }, [ldgrItems]);

  const handleLdgrSelect = useCallback((item) => {
    setSelectedLdgrId(prev => prev === item.id ? null : item.id);
    setSelectedStmtId(null);
    if (item.matchStatus !== "matched") {
      const counter = item.matchId ? stmtItems.find(s => s.id === item.matchId) : null;
      setBreakDrawer({ item, counterItem: counter, side: "ldgr" });
    } else {
      setBreakDrawer(null);
    }
  }, [stmtItems]);

  const closeDrawer = useCallback(() => {
    setBreakDrawer(null);
    setSelectedStmtId(null);
    setSelectedLdgrId(null);
  }, []);

  const handleResolve = useCallback((action) => {
    if (!breakDrawer) return;
    const { item, side } = breakDrawer;
    const counterMatchId = item.matchId;

    if (action === "accept-timing" || action === "create-je") {
      // Mark as matched on both sides
      setStmtItems(prev => prev.map(i => {
        if (side === "stmt" && i.id === item.id) return { ...i, matchStatus: "matched" };
        if (side === "ldgr" && counterMatchId && i.id === counterMatchId) return { ...i, matchStatus: "matched" };
        return i;
      }));
      setLdgrItems(prev => prev.map(i => {
        if (side === "ldgr" && i.id === item.id) return { ...i, matchStatus: "matched" };
        if (side === "stmt" && counterMatchId && i.id === counterMatchId) return { ...i, matchStatus: "matched" };
        return i;
      }));
    }
    closeDrawer();
  }, [breakDrawer, closeDrawer]);

  // Metrics
  const stmtMatched  = stmtItems.filter(i => i.matchStatus === "matched").length;
  const stmtBreaks   = stmtItems.filter(i => i.matchStatus === "break").length;
  const stmtTiming   = stmtItems.filter(i => i.matchStatus === "timing").length;
  const ldgrMatched  = ldgrItems.filter(i => i.matchStatus === "matched").length;
  const ldgrBreaks   = ldgrItems.filter(i => i.matchStatus === "break").length;
  const ldgrTiming   = ldgrItems.filter(i => i.matchStatus === "timing").length;
  const totalBreaks  = stmtBreaks + ldgrBreaks + stmtTiming + ldgrTiming;
  const stmtTotal    = stmtItems.reduce((s, i) => s + i.amount, 0);
  const ldgrTotal    = ldgrItems.reduce((s, i) => s + i.amount, 0);
  const difference   = stmtTotal - ldgrTotal;
  const isReconciled = Math.abs(difference) < 0.005 && totalBreaks === 0;

  const navSections = [
    { label: "Close", items: [
      { id: "reconcile",  label: "Reconciliation", icon: "../../assets/icons/archive-health.svg" },
      { id: "ledger",     label: "General Ledger",  icon: "../../assets/icons/run-ledger.svg"     },
      { id: "statements", label: "Statements",      icon: "../../assets/icons/governance.svg"     },
      { id: "taxlots",    label: "Tax Lots",         icon: "../../assets/icons/account-portfolio.svg" },
    ]},
    { label: "Output", items: [
      { id: "export", label: "Exports",     icon: "../../assets/icons/data-export.svg"    },
      { id: "audit",  label: "Audit Trail", icon: "../../assets/icons/retention-assurance.svg" },
    ]},
  ];

  const statusItems = [
    { label: "Statement",  value: stmtItems.length + " items", status: "ok" },
    { label: "Ledger",     value: ldgrItems.length + " items", status: "ok" },
    { label: "Matched",    value: stmtMatched + " pairs",      status: stmtMatched > 0 ? "ok" : "warn" },
    { label: "Breaks",     value: totalBreaks > 0 ? totalBreaks + " open" : "clear", status: totalBreaks > 0 ? "warn" : "ok" },
    { label: "Difference", value: Math.abs(difference) < 0.005 ? "zero" : "$" + Math.abs(difference).toFixed(2), status: Math.abs(difference) < 0.005 ? "ok" : "warn", push: true },
    { label: "Period",     value: period + " · open" },
  ];

  return (
    <div style={{ height:"100vh", display:"flex", flexDirection:"column", background:"var(--bg)", fontFamily:"var(--font-body)", color:"var(--text-secondary)", fontSize:13 }}>
      <WorkstationTopbar
        moduleLabel="Accounting"
        environment="PAPER"
        clock="14:32:08 UTC"
        brandSrc="../../assets/brand/meridian-mark-light.svg"
      />

      <div style={{ display:"flex", flex:1, minHeight:0 }}>
        <NavRail activeId="reconcile" onSelect={() => {}} sections={navSections} />

        <main style={{ flex:1, minWidth:0, overflowY:"auto", padding:16, display:"flex", flexDirection:"column", gap:12 }}>

          {/* Page header */}
          <div style={{ display:"flex", alignItems:"center", gap:10 }}>
            <h1 style={{ font:"600 22px var(--font-display)", margin:0, color:"var(--text-primary)" }}>
              Reconciliation
            </h1>
            <Badge variant="paper" dot>PAPER</Badge>
            <span style={{ fontFamily:"var(--font-data)", fontSize:11, color:"var(--text-muted)", marginLeft:2 }}>
              Statement vs. Ledger · {period}
            </span>
            <div style={{ flex:1 }} />
            <Button variant="ghost" size="sm">Export</Button>
            <Button variant="ghost" size="sm">Print report</Button>
            <Button
              variant={totalBreaks > 0 ? "primary" : "ghost"}
              size="sm"
              onClick={() => {
                setStmtItems(p => p.map(i => ({ ...i, matchStatus: i.matchId ? "matched" : i.matchStatus })));
                setLdgrItems(p => p.map(i => ({ ...i, matchStatus: i.matchId ? "matched" : i.matchStatus })));
              }}
            >
              Auto-match{totalBreaks > 0 ? ` (${stmtTiming + ldgrTiming} timing)` : ""}
            </Button>
          </div>

          {/* Controls */}
          <div className="spr-controls">
            <span className="spr-controls__label">Period</span>
            <div className="spr-controls__sel">
              <Select
                options={PERIODS}
                value={period}
                onChange={setPeriod}
              />
            </div>
            <span className="spr-controls__label" style={{ marginLeft:4 }}>Account</span>
            <div className="spr-controls__sel">
              <Select
                options={ACCOUNTS}
                value={account}
                onChange={setAccount}
              />
            </div>
            <div style={{ flex:1 }} />
            <div className="spr-seg" role="group" aria-label="Filter items">
              {[
                { key:"all",     label:"All"     },
                { key:"matched", label:"Matched" },
                { key:"open",    label:"Open"    },
              ].map(f => (
                <button key={f.key} aria-pressed={filter === f.key} onClick={() => setFilter(f.key)}>
                  {f.label}
                </button>
              ))}
            </div>
          </div>

          {/* Metrics */}
          <div className="spr-metrics">
            <MetricCard
              label="Matched pairs"
              value={String(stmtMatched)}
              delta={stmtMatched === stmtItems.filter(i => i.matchId).length ? "All eligible" : (stmtItems.length - stmtMatched) + " pending"}
              tone="ok"
            />
            <MetricCard
              label="Open breaks"
              value={String(totalBreaks)}
              delta={totalBreaks === 0 ? "No breaks" : stmtTiming + ldgrTiming + " timing · " + (stmtBreaks + ldgrBreaks) + " hard"}
              tone={totalBreaks > 0 ? "warn" : "ok"}
            />
            <MetricCard
              label="Statement total"
              value={"$" + Math.abs(stmtTotal).toLocaleString("en-US", { minimumFractionDigits:2, maximumFractionDigits:2 })}
              delta={stmtItems.length + " items · USD"}
              tone="neutral"
            />
            <MetricCard
              label="Difference"
              value={Math.abs(difference) < 0.005 ? "$0.00" : (difference < 0 ? "−" : "+") + "$" + Math.abs(difference).toFixed(2)}
              delta={isReconciled ? "Fully reconciled" : "Statement − Ledger"}
              tone={Math.abs(difference) < 0.005 ? "ok" : "warn"}
            />
          </div>

          {/* Split pane + summary */}
          <div style={{ display:"flex", flexDirection:"column", flex:1, minHeight:0, minHeight:420 }}>
            <div className="spr-split" style={{ flex:1, minHeight:0 }}>
              <Pane
                title="Custodian Statement"
                items={filteredStmt}
                matchedCount={filteredStmt.filter(i => i.matchStatus === "matched").length}
                breakCount={filteredStmt.filter(i => i.matchStatus === "break").length}
                timingCount={filteredStmt.filter(i => i.matchStatus === "timing").length}
                selectedId={selectedStmtId}
                crossId={crossLitStmtId}
                onSelect={handleStmtSelect}
                currency="USD"
              />
              <Pane
                title="Internal Ledger"
                items={filteredLdgr}
                matchedCount={filteredLdgr.filter(i => i.matchStatus === "matched").length}
                breakCount={filteredLdgr.filter(i => i.matchStatus === "break").length}
                timingCount={filteredLdgr.filter(i => i.matchStatus === "timing").length}
                selectedId={selectedLdgrId}
                crossId={crossLitLdgrId}
                onSelect={handleLdgrSelect}
                currency="USD"
              />
            </div>

            {/* Summary bar */}
            <div className="spr-summary">
              <div className="spr-sum-cell">
                <span className="spr-sum-label">Statement balance</span>
                <AmountCell value={stmtTotal} currency="USD" parens strong />
              </div>
              <div className="spr-sum-cell">
                <span className="spr-sum-label">Ledger balance</span>
                <AmountCell value={ldgrTotal} currency="USD" parens strong />
              </div>
              <div className={`spr-status-badge ${isReconciled ? "spr-status-badge--rec" : "spr-status-badge--out"}`}>
                <span className="spr-status-dot-lg" />
                {isReconciled
                  ? "Reconciled"
                  : <>Out by <AmountCell value={Math.abs(difference)} currency="USD" strong style={{ color:"inherit", marginLeft:4 }} /></>
                }
              </div>
            </div>
          </div>

        </main>
      </div>

      <StatusBar items={statusItems} />

      {breakDrawer && (
        <BreakDrawer
          item={breakDrawer.item}
          counterItem={breakDrawer.counterItem}
          side={breakDrawer.side}
          onClose={closeDrawer}
          onResolve={handleResolve}
        />
      )}
    </div>
  );
}

window.SplitPaneReconciliationScreen = SplitPaneReconciliationScreen;
if (typeof module !== "undefined") module.exports = { SplitPaneReconciliationScreen };
