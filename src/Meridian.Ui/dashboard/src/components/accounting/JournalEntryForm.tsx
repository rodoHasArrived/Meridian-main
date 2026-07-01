// Meridian journal-entry form — balanced double-entry input. A header (date / reference / memo)
// sits over a line grid of Account · Debit · Credit with live column totals and a balance gauge
// that stays red ("Out by …") until Σdebit = Σcredit, then turns green ("Balanced"). Add/remove
// lines; an optional account list drives autocomplete; the Post button is gated on a balanced
// entry. Self-contained (native inputs styled with Concrete tokens) so it has no cross-unit deps.
import { useState } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface JournalLine {
  account?: string;
  debit?: number | string;
  credit?: number | string;
}

export interface JournalHeader {
  date?: string;
  ref?: string;
  memo?: string;
}

export interface JournalEntry {
  header: JournalHeader;
  lines: JournalLine[];
}

export interface JournalEntryFormProps {
  /** Seed lines. @default two blank lines */
  initialLines?: JournalLine[];
  /** Seed header fields. */
  initialHeader?: JournalHeader;
  /** Account names for the input datalist (autocomplete). */
  accounts?: string[];
  /** @default "USD" */
  currency?: string;
  /** Absolute debit−credit difference treated as balanced. @default 0.005 */
  tolerance?: number;
  /** Fires on every edit with the full { header, lines } state. */
  onChange?: (entry: JournalEntry) => void;
  /** When set, renders a Post button (enabled only when balanced). */
  onPost?: (entry: JournalEntry) => void;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.jnl{border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-card,2px);
  background:var(--bg-light,#FFFFFF);overflow:hidden;}
.jnl__hd{display:grid;grid-template-columns:140px 160px 1fr;gap:12px;padding:14px;
  border-bottom:1px solid var(--border,#CBD3DC);background:var(--bg-medium,#EBEFF4);}
.jnl__field{display:flex;flex-direction:column;gap:4px;min-width:0;}
.jnl__lbl{font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);}
.jnl__inp{font-family:var(--font-body,inherit);font-size:12px;color:var(--text-primary,#22272E);
  background:var(--bg-light,#FFFFFF);border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  padding:5px 8px;line-height:1.4;width:100%;box-sizing:border-box;}
.jnl__inp:focus{outline:2px solid var(--accent,#2F6F8F);outline-offset:-1px;border-color:var(--accent,#2F6F8F);}
.jnl__inp--num{font-family:var(--font-data,monospace);text-align:right;font-variant-numeric:tabular-nums;}
.jnl__grid{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data,monospace);font-size:12px;}
.jnl__grid th{padding:8px 12px;text-align:left;font-family:var(--font-body,inherit);font-size:10px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border,#CBD3DC);}
.jnl__grid th.jnl--r{text-align:right;}
.jnl__grid th.jnl--x{width:36px;}
.jnl__grid td{padding:5px 12px;border-top:1px solid var(--border,#CBD3DC);vertical-align:middle;}
.jnl__grid tbody tr:first-child td{border-top:none;}
.jnl__grid td.jnl--r{text-align:right;}
.jnl__del{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;
  border:1px solid transparent;border-radius:var(--radius-button,2px);background:transparent;cursor:pointer;
  color:var(--text-muted,#59636F);font-size:14px;line-height:1;transition:all .12s ease;}
.jnl__del:hover{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.jnl__grid tfoot td{padding:8px 12px;border-top:2px solid var(--border-strong,#99A5B2);
  background:var(--bg-medium,#EBEFF4);font-weight:600;}
.jnl__grid tfoot td.jnl--r{text-align:right;}
.jnl__foot-lbl{font-family:var(--font-body,inherit);font-size:11px;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
.jnl__bar{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 14px;border-top:1px solid var(--border,#CBD3DC);}
.jnl__actions{display:flex;gap:8px;}
.jnl__btn{font-family:var(--font-body,inherit);font-size:12px;font-weight:600;cursor:pointer;
  border-radius:var(--radius-button,2px);padding:6px 12px;line-height:1.3;border:1px solid;transition:all .12s ease;}
.jnl__btn--ghost{background:transparent;border-color:var(--border,#CBD3DC);color:var(--text-secondary,#4D5967);}
.jnl__btn--ghost:hover{background:var(--bg-hover,#F3F6F9);border-color:var(--border-strong,#99A5B2);}
.jnl__btn--primary{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);color:#fff;}
.jnl__btn--primary:hover:not(:disabled){background:var(--accent-pressed,#255B75);border-color:var(--accent-pressed,#255B75);}
.jnl__btn:disabled{cursor:not-allowed;opacity:.5;}
.jnl__status{display:inline-flex;align-items:center;gap:7px;
  font-family:var(--font-body,inherit);font-size:11px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
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

const blankLine = (): JournalLine => ({ account: "", debit: "", credit: "" });

export function JournalEntryForm({
  initialLines,
  initialHeader,
  accounts,
  currency = "USD",
  tolerance = 0.005,
  onChange,
  onPost
}: JournalEntryFormProps) {
  inject();
  const [header, setHeader] = useState<JournalHeader>(() => ({ date: "", ref: "", memo: "", ...initialHeader }));
  const [lines, setLines] = useState<JournalLine[]>(() =>
    initialLines && initialLines.length ? initialLines.map((l) => ({ ...blankLine(), ...l })) : [blankLine(), blankLine()]
  );

  const emit = (h: JournalHeader, l: JournalLine[]) => onChange?.({ header: h, lines: l });
  const setH = (k: keyof JournalHeader, v: string) => {
    const h = { ...header, [k]: v };
    setHeader(h);
    emit(h, lines);
  };
  const setL = (i: number, k: keyof JournalLine, v: string) => {
    const l = lines.map((row, idx) => (idx === i ? { ...row, [k]: v } : row));
    setLines(l);
    emit(header, l);
  };
  const addLine = () => {
    const l = [...lines, blankLine()];
    setLines(l);
    emit(header, l);
  };
  const delLine = (i: number) => {
    const l = lines.length > 1 ? lines.filter((_, idx) => idx !== i) : [blankLine()];
    setLines(l);
    emit(header, l);
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
          <label className="jnl__lbl" htmlFor="jnl-date">
            Date
          </label>
          <input id="jnl-date" className="jnl__inp" type="date" value={header.date} onChange={(e) => setH("date", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl" htmlFor="jnl-ref">
            Reference
          </label>
          <input id="jnl-ref" className="jnl__inp" placeholder="JE-0001" value={header.ref} onChange={(e) => setH("ref", e.target.value)} />
        </div>
        <div className="jnl__field">
          <label className="jnl__lbl" htmlFor="jnl-memo">
            Memo
          </label>
          <input id="jnl-memo" className="jnl__inp" placeholder="Description" value={header.memo} onChange={(e) => setH("memo", e.target.value)} />
        </div>
      </div>

      {dlId && (
        <datalist id={dlId}>
          {accounts!.map((a) => (
            <option key={a} value={a} />
          ))}
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
                <input
                  className="jnl__inp"
                  placeholder="Account"
                  value={row.account}
                  onChange={(e) => setL(i, "account", e.target.value)}
                  list={dlId}
                  aria-label={`Line ${i + 1} account`}
                />
              </td>
              <td className="jnl--r">
                <input
                  className="jnl__inp jnl__inp--num"
                  type="number"
                  placeholder="0.00"
                  value={row.debit as string | number}
                  onChange={(e) => setL(i, "debit", e.target.value)}
                  aria-label={`Line ${i + 1} debit`}
                />
              </td>
              <td className="jnl--r">
                <input
                  className="jnl__inp jnl__inp--num"
                  type="number"
                  placeholder="0.00"
                  value={row.credit as string | number}
                  onChange={(e) => setL(i, "credit", e.target.value)}
                  aria-label={`Line ${i + 1} credit`}
                />
              </td>
              <td className="jnl--r">
                <button type="button" className="jnl__del" aria-label={`Remove line ${i + 1}`} onClick={() => delLine(i)}>
                  ×
                </button>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="jnl__foot-lbl">Totals</td>
            <td className="jnl--r">
              <AmountCell value={totalD} currency={currency} strong />
            </td>
            <td className="jnl--r">
              <AmountCell value={totalC} currency={currency} strong />
            </td>
            <td />
          </tr>
        </tfoot>
      </table>

      <div className="jnl__bar">
        <div className="jnl__actions">
          <button type="button" className="jnl__btn jnl__btn--ghost" onClick={addLine}>
            + Add line
          </button>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span className={`jnl__status ${balanced ? "jnl__status--bal" : "jnl__status--out"}`} role="status">
            <span className="jnl__dot" aria-hidden="true" />
            {balanced ? (
              "Balanced"
            ) : (
              <>
                Out by <AmountCell value={Math.abs(diff)} currency={currency} strong style={{ color: "inherit" }} />
              </>
            )}
          </span>
          {onPost && (
            <button type="button" className="jnl__btn jnl__btn--primary" disabled={!balanced} onClick={() => onPost({ header, lines })}>
              Post entry
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

JournalEntryForm.displayName = "JournalEntryForm";
