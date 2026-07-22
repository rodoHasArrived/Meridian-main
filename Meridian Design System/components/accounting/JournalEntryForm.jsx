// Meridian journal-entry form — balanced double-entry input. Header (date / reference / memo)
// over a line grid of Account · Debit · Credit, with live column totals and a balance gauge that
// stays red until Σdebit = Σcredit. Uses Core Input, Select, Button, DatePicker primitives.
import React from "react";
const { useState } = React;
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";
import { Input } from "../core/Input";
import { Select } from "../core/Select";
import { Button } from "../core/Button";
import { DatePicker } from "../core/DatePicker";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.jnl{border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,2px);
  background:var(--bg-light,#fff);box-shadow:var(--shadow-card,none);overflow:hidden;}
.jnl__hd{display:grid;grid-template-columns:140px 160px 1fr;gap:12px;padding:14px;
  border-bottom:1px solid var(--border,#D7DCE2);background:var(--bg-medium,#F5F7FA);}
.jnl__grid{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data);font-size:12px;}
.jnl__grid th{padding:8px 12px;text-align:left;font-family:var(--font-body);font-size:10px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border,#D7DCE2);}
.jnl__grid th.jnl--r{text-align:right;}
.jnl__grid th.jnl--x{width:36px;}
.jnl__grid td{padding:5px 12px;border-top:1px solid var(--border,#D7DCE2);vertical-align:middle;}
.jnl__grid tbody tr:first-child td{border-top:none;}
.jnl__grid td.jnl--r{text-align:right;}
.jnl__del{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;
  border:1px solid transparent;border-radius:var(--radius-button,2px);background:transparent;cursor:pointer;
  color:var(--text-muted,#59636F);font-size:14px;line-height:1;transition:all .12s ease;}
.jnl__del:hover{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.jnl__grid tfoot td{padding:8px 12px;border-top:2px solid var(--border-strong,#AAB4BF);
  background:var(--bg-medium,#F5F7FA);font-weight:600;}
.jnl__grid tfoot td.jnl--r{text-align:right;}
.jnl__foot-lbl{font-family:var(--font-body);font-size:11px;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
.jnl__bar{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 14px;border-top:1px solid var(--border,#D7DCE2);}
.jnl__actions{display:flex;gap:8px;}
.jnl__status{display:inline-flex;align-items:center;gap:7px;
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  border:1px solid;border-radius:var(--radius-chip,2px);padding:5px 10px;}
.jnl__status--bal{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#10663F);}
.jnl__status--out{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.jnl__dot{height:6px;width:6px;border-radius:50%;background:currentColor;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "journal");
  el.textContent = css;
  document.head.appendChild(el);
}

const blankLine = () => ({ account: "", debit: "", credit: "" });

export function JournalEntryForm({
  initialLines,
  initialHeader,
  accounts,                // optional string[] → datalist autocomplete
  currency = "USD",
  tolerance = 0.005,
  onChange,
  onPost,
}) {
  inject();
  const [header, setHeader] = useState(() => ({ date: "", ref: "", memo: "", ...initialHeader }));
  const [lines, setLines] = useState(() =>
    initialLines && initialLines.length ? initialLines.map((l) => ({ ...blankLine(), ...l })) : [blankLine(), blankLine()]
  );

  const emit = (h, l) => onChange && onChange({ header: h, lines: l });
  const setH = (k, v) => { const h = { ...header, [k]: v }; setHeader(h); emit(h, lines); };
  const setL = (i, k, v) => {
    const l = lines.map((row, idx) => (idx === i ? { ...row, [k]: v } : row));
    setLines(l); emit(header, l);
  };
  const addLine = () => { const l = [...lines, blankLine()]; setLines(l); emit(header, l); };
  const delLine = (i) => {
    const l = lines.length > 1 ? lines.filter((_, idx) => idx !== i) : [blankLine()];
    setLines(l); emit(header, l);
  };

  const totalD = lines.reduce((a, r) => a + (toNumber(r.debit) || 0), 0);
  const totalC = lines.reduce((a, r) => a + (toNumber(r.credit) || 0), 0);
  const diff = totalD - totalC;
  const balanced = Math.abs(diff) <= tolerance && (totalD > 0 || totalC > 0);
  const dlId = accounts && accounts.length ? "jnl-accts" : undefined;

  return (
    <div className="jnl" role="group" aria-label="Journal entry">
      <div className="jnl__hd">
        <div className="jnl__field">
          <label className="jnl__lbl">Date</label>
          <Input type="date" value={header.date}
            onChange={(e) => setH("date", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl">Reference</label>
          <Input placeholder="JE-0001" value={header.ref}
            onChange={(e) => setH("ref", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl">Memo</label>
          <Input placeholder="Description" value={header.memo}
            onChange={(e) => setH("memo", e.target.value)} />
        </div>
      </div>

      {dlId && (
        <datalist id={dlId}>
          {accounts.map((a) => <option key={a} value={a} />)}
        </datalist>
      )}

      <table className="jnl__grid">
        <thead>
          <tr>
            <th>Account</th>
            <th className="jnl--r">Debit</th>
            <th className="jnl--r">Credit</th>
            <th className="jnl--x" aria-label="Remove" />
          </tr>
        </thead>
        <tbody>
          {lines.map((row, i) => (
            <tr key={i}>
              <td>
                <Input placeholder="Account" value={row.account}
                  onChange={(e) => setL(i, "account", e.target.value)} list={dlId} />
              </td>
              <td className="jnl--r">
                <Input type="number" placeholder="0.00" value={row.debit}
                  onChange={(e) => setL(i, "debit", e.target.value)} style={{ textAlign: "right" }} />
              </td>
              <td className="jnl--r">
                <Input type="number" placeholder="0.00" value={row.credit}
                  onChange={(e) => setL(i, "credit", e.target.value)} style={{ textAlign: "right" }} />
              </td>
              <td className="jnl--r">
                <button type="button" className="jnl__del" aria-label={`Remove line ${i + 1}`}
                  onClick={() => delLine(i)}>&times;</button>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="jnl__foot-lbl">Totals</td>
            <td className="jnl--r"><AmountCell value={totalD} currency={currency} strong /></td>
            <td className="jnl--r"><AmountCell value={totalC} currency={currency} strong /></td>
            <td />
          </tr>
        </tfoot>
      </table>

      <div className="jnl__bar">
        <div className="jnl__actions">
          <Button variant="ghost" onClick={addLine}>+ Add line</Button>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span className={`jnl__status ${balanced ? "jnl__status--bal" : "jnl__status--out"}`}>
            <span className="jnl__dot" aria-hidden="true" />
            {balanced
              ? "Balanced"
              : <>Out by <AmountCell value={Math.abs(diff)} currency={currency} strong style={{ color: "inherit" }} /></>}
          </span>
          {onPost && (
            <Button variant="primary" disabled={!balanced}
              onClick={() => onPost({ header, lines })}>Post entry</Button>
          )}
        </div>
      </div>
    </div>
  );
}
