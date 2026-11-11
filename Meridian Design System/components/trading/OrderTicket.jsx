// Meridian OrderTicket — order-entry primitive. Side, qty, type, limit, TIF, notional
// summary, and a hard confirm gate when environment="live" — real money requires an explicit
// check before the submit button enables. Owns its draft state; emits onSubmit(order).
import React from "react";
import { formatMoney } from "../core/format.js";
import { Button } from "../core/Button";
import { Badge } from "../core/Badge";
import { Input } from "../core/Input";
import { Select } from "../core/Select";
import { ActionGuardList, summarizeGuards } from "../operations/ActionGuard";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ticket{display:flex;flex-direction:column;gap:10px;border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);padding:14px;font-family:var(--font-body);box-sizing:border-box;}
.mds-ticket__hd{display:flex;align-items:center;justify-content:space-between;gap:8px;}
.mds-ticket__sym{font:600 14px var(--font-data);color:var(--text-primary,#20252B);letter-spacing:.02em;}
.mds-ticket__last{font:11px var(--font-data);color:var(--text-muted,#6B737B);font-variant-numeric:tabular-nums;}
.mds-ticket__side{display:grid;grid-template-columns:1fr 1fr;}
.mds-ticket__sbtn{appearance:none;cursor:pointer;padding:7px 0;font-family:var(--font-body);font-size:12px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;border:1px solid var(--border,#D2CDC3);
  background:var(--bg-light,#F8F7F3);color:var(--text-secondary,#535C65);}
.mds-ticket__sbtn+.mds-ticket__sbtn{border-left:none;}
.mds-ticket__sbtn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-ticket__sbtn--buy[aria-pressed="true"]{background:var(--green-a10,rgba(74,124,94,.10));
  border-color:var(--green,#2E7655);color:var(--green-dim,#375D46);}
.mds-ticket__sbtn--sell[aria-pressed="true"]{background:var(--red-a10,rgba(166,67,63,.10));
  border-color:var(--red,#B54B42);color:var(--red-dim,#7D322F);}
.mds-ticket__grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;align-items:start;}
/* Converge on the form primitives; only right-align the numeric fields. */
.mds-ticket__num input{text-align:right;font-variant-numeric:tabular-nums;}
.mds-ticket__sum{display:flex;justify-content:space-between;align-items:baseline;padding:8px 10px;
  border:1px solid var(--border-divider,#E2DED5);background:var(--card-surface-raised,#FCFBF8);
  font:12px var(--font-data);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-ticket__sum-label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6B737B);}
.mds-ticket__gate{display:flex;gap:7px;align-items:flex-start;padding:8px 10px;
  border:1px solid var(--red,#B54B42);background:var(--red-a10,rgba(166,67,63,.10));
  font-size:11px;line-height:1.4;color:var(--red-dim,#7D322F);}
.mds-ticket__gate input{margin:1px 0 0;accent-color:var(--red,#B54B42);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "orderticket");
  el.textContent = css;
  document.head.appendChild(el);
}

const fmtMoney = (n) =>
  formatMoney(Math.abs(n), { currency: "USD", decimals: 2 });

// Order quantities and prices must be whole decimal entries, never numeric prefixes.
// In particular, parseFloat("10abc") must not turn an invalid draft into an order.
function positiveDecimal(value) {
  const text = String(value ?? "").trim();
  if (!/^(?:\d+(?:\.\d*)?|\.\d+)$/.test(text)) return null;
  const number = Number(text);
  return Number.isFinite(number) && number > 0 ? number : null;
}

export function OrderTicket({
  symbol = "",
  lastPrice = null,
  environment = "paper",
  defaultSide = "buy",
  tifOptions = ["DAY", "GTC", "IOC", "FOK"],
  priceDecimals = 2,
  prefill = null,
  guards = [],
  onDraftChange,
  onSubmit,
}) {
  inject();
  const [side, setSide] = React.useState(prefill?.side || defaultSide);
  const [qty, setQty] = React.useState(prefill?.qty != null ? String(prefill.qty) : "");
  const [type, setType] = React.useState(prefill?.type || "market");
  // Prices carry the instrument's full tick precision: a ticket that reads 201.1 where the venue
  // quotes 201.10 invites a misread at the one moment misreads are expensive.
  const digits = Number.isInteger(priceDecimals) && priceDecimals >= 0 && priceDecimals <= 20 ? priceDecimals : 2;
  const atTick = (v) => { const n = positiveDecimal(v); return n != null ? n.toFixed(digits) : String(v ?? ""); };
  const [limit, setLimit] = React.useState(prefill?.limitPrice != null ? atTick(prefill.limitPrice) : "");
  const [tif, setTif] = React.useState(prefill?.tif || tifOptions[0]);
  const [confirmedDraft, setConfirmedDraft] = React.useState(null);
  // A prefill lands only when its token changes, so a ladder click or an amend can seed the
  // draft without the ticket fighting the operator's own typing on every re-render.
  const token = prefill ? String(prefill.token ?? `${prefill.side}|${prefill.qty}|${prefill.type}|${prefill.limitPrice}|${prefill.tif}`) : null;
  const seen = React.useRef(token);
  React.useEffect(() => {
    if (token == null || seen.current === token) return;
    seen.current = token;
    if (prefill.side) setSide(prefill.side);
    if (prefill.qty != null) setQty(String(prefill.qty));
    if (prefill.type) setType(prefill.type);
    if (prefill.limitPrice != null) setLimit(atTick(prefill.limitPrice));
    if (prefill.tif) setTif(prefill.tif);
    setConfirmedDraft(null);
  }, [token]);
  const env = String(environment).trim().toLowerCase();
  const live = env === "live";
  const knownEnvironment = ["live", "paper", "fixture"].includes(env);
  const q = positiveDecimal(qty);
  const limitPrice = positiveDecimal(limit);
  const px = type === "limit" ? limitPrice : lastPrice;
  const estimate = q != null && Number.isFinite(px) && px > 0 ? q * px : null;
  const notional = Number.isFinite(estimate) ? estimate : null;
  const valid = typeof symbol === "string" && symbol.trim().length > 0 && knownEnvironment && q != null &&
    ["buy", "sell"].includes(side) && tifOptions.includes(tif) &&
    (type === "market" || (type === "limit" && limitPrice != null));
  // Acknowledgement belongs to this draft, including externally supplied identity
  // and prefill context. A changing quote alone does not change the submitted order.
  const draftKey = JSON.stringify([symbol, side, qty, type, limit, tif, env, token]);
  const confirmLive = confirmedDraft === draftKey;
  React.useEffect(() => { setConfirmedDraft(null); }, [draftKey]);
  const guardSummary = summarizeGuards(guards);
  const canSubmit = valid && typeof onSubmit === "function" && !guardSummary.blocked && (!live || confirmLive);
  const dirty = qty !== "" || limit !== "" || type !== "market";
  React.useEffect(() => {
    onDraftChange?.({ symbol, side, qty: q, type, limitPrice: type === "limit" ? limitPrice : null, tif, notional, valid });
  }, [symbol, side, qty, type, limit, tif, notional, valid]);
  const clear = () => { setQty(""); setLimit(""); setType("market"); setConfirmedDraft(null); };
  const submit = () => {
    if (!canSubmit || !onSubmit) return;
    setConfirmedDraft(null);
    onSubmit({
      symbol, side, qty: q, type,
      limitPrice: type === "limit" ? limitPrice : null,
      tif, environment: env, notional,
    });
    clear();
  };
  return (
    <div className="mds-ticket">
      <div className="mds-ticket__hd">
        <span className="mds-ticket__sym">{symbol || "—"}</span>
        <span className="mds-ticket__last">{Number.isFinite(lastPrice) ? `last ${lastPrice.toFixed(4)}` : ""}</span>
        <Badge variant={knownEnvironment ? env : "warning"} dot>{env.toUpperCase() || "UNKNOWN"}</Badge>
      </div>
      {!knownEnvironment && <span role="status">Unknown environment — order submission disabled.</span>}
      <div className="mds-ticket__side" role="group" aria-label="Order side">
        <button type="button" className="mds-ticket__sbtn mds-ticket__sbtn--buy" aria-pressed={side === "buy"} onClick={() => setSide("buy")}>Buy</button>
        <button type="button" className="mds-ticket__sbtn mds-ticket__sbtn--sell" aria-pressed={side === "sell"} onClick={() => setSide("sell")}>Sell</button>
      </div>
      <div className="mds-ticket__grid">
        <div className="mds-ticket__num">
          <Input label="Quantity" inputMode="decimal" value={qty}
            error={qty !== "" && q == null ? "Enter a positive decimal quantity." : undefined}
            onChange={(e) => setQty(e.target.value)} placeholder="0" />
        </div>
        <Select label="Type" value={type} onChange={setType}
          options={[{ value: "market", label: "Market" }, { value: "limit", label: "Limit" }]} />
        <div className="mds-ticket__num">
          <Input label="Limit price" inputMode="decimal" value={limit} disabled={type !== "limit"}
            error={type === "limit" && limit !== "" && limitPrice == null ? "Enter a positive decimal price." : undefined}
            onChange={(e) => setLimit(e.target.value)} onBlur={() => limit !== "" && setLimit(atTick(limit))}
            placeholder={type === "limit" ? (0).toFixed(digits) : "—"} />
        </div>
        <Select label="Time in force" value={tif} onChange={setTif} options={tifOptions} />
      </div>
      <div className="mds-ticket__sum">
        <span className="mds-ticket__sum-label">Est. notional</span>
        <span>{notional != null ? fmtMoney(notional) : "—"}</span>
      </div>
      {guards.length > 0 && (
        <ActionGuardList guards={guards} label="Pre-trade checks" />
      )}
      {live && (
        <label className="mds-ticket__gate">
          <input type="checkbox" checked={confirmLive}
            onChange={(e) => setConfirmedDraft(e.target.checked ? draftKey : null)} />
          <span>Live environment — this order executes against real capital. Confirm to enable submit.</span>
        </label>
      )}
      <div style={{ display: "flex", gap: 8 }}>
        <Button variant="primary" disabled={!canSubmit} onClick={submit} style={{ flex: 1 }}>
          Submit {side} order
        </Button>
        {dirty && <Button variant="ghost" onClick={clear}>Clear</Button>}
      </div>
      {guardSummary.blocked && (
        <div style={{ font: "11px var(--font-body)", color: "var(--red-dim,#7D322F)", lineHeight: 1.4 }}>
          Blocked — {guardSummary.firstBlocking?.reason || guardSummary.firstBlocking?.label}
        </div>
      )}
    </div>
  );
}
