// Meridian journal-entry form — balanced double-entry input. Header (date / reference / memo)
// over a line grid of Account · Debit · Credit, with live column totals and a balance gauge that
// stays red until Σdebit = Σcredit. Self-contained styling so it can be lifted into any screen.
import React, { useState } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.jnl{border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,8px);
  background:var(--bg-light,#fff);box-shadow:var(--shadow-card,0 1px 1px rgba(0,0,0,.08));overflow:hidden;}
.jnl__hd{display:grid;grid-template-columns:140px 160px 1fr;gap:12px;padding:14px;
  border-bottom:1px solid var(--border,#D7DCE2);background:var(--bg-medium,#F5F7FA);}
.jnl__field{display:flex;flex-direction:column;gap:5px;min-width:0;}
.jnl__lbl{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);}
.jnl__in{width:100%;box-sizing:border-box;height:32px;padding:6px 9px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-button,6px);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);
  font-family:var(--font-data);font-size:12px;font-variant-numeric:tabular-nums;
  transition:border-color .12s ease,box-shadow .12s ease;}
.jnl__in:hover{border-color:var(--border-hover,#B8C2CC);}
.jnl__in:focus{outline:none;border-color:var(--border-focus,#2F6F8F);box-shadow:0 0 0 2px rgba(47,111,143,.20);}
.jnl__in--num{text-align:right;}
.jnl__in::placeholder{color:var(--text-disabled,#9AA4AF);}
.jnl__grid{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data);font-size:12px;}
.jnl__grid th{padding:8px 12px;text-align:left;font-family:var(--font-body);font-size:10px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);}
.jnl__grid th.jnl--r{text-align:right;}
.jnl__grid th.jnl--x{width:36px;}
.jnl__grid td{padding:5px 12px;border-top:1px solid var(--border,#D7DCE2);vertical-align:middle;}
.jnl__grid tbody tr:first-child td{border-top:none;}
.jnl__grid td.jnl--r{text-align:right;}
.jnl__del{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;
  border:1px solid transparent;border-radius:var(--radius-button,6px);background:transparent;cursor:pointer;
  color:var(--text-muted,#6E7781);font-size:14px;line-height:1;transition:all .12s ease;}
.jnl__del:hover{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#983244);}
.jnl__grid tfoot td{padding:8px 12px;border-top:2px solid var(--border-strong,#AAB4BF);
  background:var(--bg-medium,#F5F7FA);font-weight:600;}
.jnl__grid tfoot td.jnl--r{text-align:right;}
.jnl__foot-lbl{font-family:var(--font-body);font-size:11px;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
.jnl__bar{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 14px;border-top:1px solid var(--border,#D7DCE2);}
.jnl__actions{display:flex;gap:8px;}
.jnl__btn{display:inline-flex;align-items:center;gap:6px;height:32px;padding:0 14px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-button,6px);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);
  font-family:var(--font-body);font-size:12px;cursor:pointer;transition:all .12s ease;}
.jnl__btn:hover{background:var(--bg-hover,#F1F4F7);border-color:var(--border-hover,#B8C2CC);}
.jnl__btn--primary{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);color:#fff;font-weight:600;}
.jnl__btn--primary:hover{background:rgba(47,111,143,.85);border-color:var(--accent,#2F6F8F);}
.jnl__btn--primary:disabled{opacity:.45;cursor:not-allowed;background:var(--accent,#2F6F8F);}
.jnl__status{display:inline-flex;align-items:center;gap:7px;
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  border:1px solid;border-radius:var(--radius-chip,4px);padding:5px 10px;}
.jnl__status--bal{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#126C4D);}
.jnl__status--out{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#983244);}
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
          <label className="jnl__lbl" htmlFor="jnl-date">Date</label>
          <input id="jnl-date" className="jnl__in" placeholder="2026-06-09" value={header.date}
            onChange={(e) => setH("date", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl" htmlFor="jnl-ref">Reference</label>
          <input id="jnl-ref" className="jnl__in" placeholder="JE-0001" value={header.ref}
            onChange={(e) => setH("ref", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl" htmlFor="jnl-memo">Memo</label>
          <input id="jnl-memo" className="jnl__in" placeholder="Description" value={header.memo}
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
                <input className="jnl__in" list={dlId} placeholder="Account" value={row.account}
                  onChange={(e) => setL(i, "account", e.target.value)} />
              </td>
              <td className="jnl--r">
                <input className="jnl__in jnl__in--num" inputMode="decimal" placeholder="0.00" value={row.debit}
                  onChange={(e) => setL(i, "debit", e.target.value)} />
              </td>
              <td className="jnl--r">
                <input className="jnl__in jnl__in--num" inputMode="decimal" placeholder="0.00" value={row.credit}
                  onChange={(e) => setL(i, "credit", e.target.value)} />
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
          <button type="button" className="jnl__btn" onClick={addLine}>+ Add line</button>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span className={`jnl__status ${balanced ? "jnl__status--bal" : "jnl__status--out"}`}>
            <span className="jnl__dot" aria-hidden="true" />
            {balanced
              ? "Balanced"
              : <>Out by <AmountCell value={Math.abs(diff)} currency={currency} strong style={{ color: "inherit" }} /></>}
          </span>
          {onPost && (
            <button type="button" className="jnl__btn jnl__btn--primary" disabled={!balanced}
              onClick={() => onPost({ header, lines })}>Post entry</button>
          )}
        </div>
      </div>
    </div>
  );
}
