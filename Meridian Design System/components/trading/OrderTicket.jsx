// Meridian OrderTicket — order-entry primitive. Side, qty, type, limit, TIF, notional
// summary, and a hard confirm gate when environment="live" — real money requires an explicit
// check before the submit button enables. Owns its draft state; emits onSubmit(order).
import React from "react";
import { Button } from "../core/Button";
import { Badge } from "../core/Badge";
import { Input } from "../core/Input";
import { Select } from "../core/Select";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ticket{display:flex;flex-direction:column;gap:10px;border:1px solid var(--border,#CBD3DC);
  background:var(--bg-light,#fff);padding:14px;font-family:var(--font-body);box-sizing:border-box;}
.mds-ticket__hd{display:flex;align-items:center;justify-content:space-between;gap:8px;}
.mds-ticket__sym{font:600 14px var(--font-data);color:var(--text-primary,#22272E);letter-spacing:.02em;}
.mds-ticket__last{font:11px var(--font-data);color:var(--text-muted,#59636F);font-variant-numeric:tabular-nums;}
.mds-ticket__side{display:grid;grid-template-columns:1fr 1fr;}
.mds-ticket__sbtn{appearance:none;cursor:pointer;padding:7px 0;font-family:var(--font-body);font-size:12px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;border:1px solid var(--border,#CBD3DC);
  background:var(--bg-light,#fff);color:var(--text-secondary,#4D5967);}
.mds-ticket__sbtn+.mds-ticket__sbtn{border-left:none;}
.mds-ticket__sbtn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:-2px;}
.mds-ticket__sbtn--buy[aria-pressed="true"]{background:var(--green-a10,rgba(22,136,95,.10));
  border-color:var(--green,#16885F);color:var(--green-dim,#10663F);}
.mds-ticket__sbtn--sell[aria-pressed="true"]{background:var(--red-a10,rgba(186,63,85,.10));
  border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.mds-ticket__grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;align-items:start;}
/* Converge on the form primitives; only right-align the numeric fields. */
.mds-ticket__num input{text-align:right;font-variant-numeric:tabular-nums;}
.mds-ticket__sum{display:flex;justify-content:space-between;align-items:baseline;padding:8px 10px;
  border:1px solid var(--border-divider,#D2D9E2);background:var(--card-surface-raised,#F3F6F9);
  font:12px var(--font-data);font-variant-numeric:tabular-nums;color:var(--text-primary,#22272E);}
.mds-ticket__sum-label{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);}
.mds-ticket__gate{display:flex;gap:7px;align-items:flex-start;padding:8px 10px;
  border:1px solid var(--red,#BA3F55);background:var(--red-a10,rgba(186,63,85,.10));
  font-size:11px;line-height:1.4;color:var(--red-dim,#8C2F40);}
.mds-ticket__gate input{margin:1px 0 0;accent-color:var(--red,#BA3F55);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "orderticket");
  el.textContent = css;
  document.head.appendChild(el);
}

const fmtMoney = (n) =>
  "$" + Math.abs(n).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function OrderTicket({
  symbol = "",
  lastPrice = null,
  environment = "paper",
  defaultSide = "buy",
  tifOptions = ["DAY", "GTC", "IOC", "FOK"],
  onSubmit,
}) {
  inject();
  const [side, setSide] = React.useState(defaultSide);
  const [qty, setQty] = React.useState("");
  const [type, setType] = React.useState("market");
  const [limit, setLimit] = React.useState("");
  const [tif, setTif] = React.useState(tifOptions[0]);
  const [confirmLive, setConfirmLive] = React.useState(false);
  const env = String(environment).toLowerCase();
  const live = env === "live";
  const q = parseFloat(qty);
  const px = type === "limit" ? parseFloat(limit) : lastPrice;
  const notional = Number.isFinite(q) && Number.isFinite(px) ? q * px : null;
  const valid = Number.isFinite(q) && q > 0 && (type === "market" || (Number.isFinite(parseFloat(limit)) && parseFloat(limit) > 0));
  const canSubmit = valid && (!live || confirmLive);
  const submit = () => {
    if (!canSubmit || !onSubmit) return;
    onSubmit({
      symbol, side, qty: q, type,
      limitPrice: type === "limit" ? parseFloat(limit) : null,
      tif, environment: env,
    });
  };
  return (
    <div className="mds-ticket">
      <div className="mds-ticket__hd">
        <span className="mds-ticket__sym">{symbol || "—"}</span>
        <span className="mds-ticket__last">{Number.isFinite(lastPrice) ? `last ${lastPrice.toFixed(4)}` : ""}</span>
        <Badge variant={live ? "live" : env === "fixture" ? "fixture" : "paper"} dot>{env.toUpperCase()}</Badge>
      </div>
      <div className="mds-ticket__side" role="group" aria-label="Order side">
        <button type="button" className="mds-ticket__sbtn mds-ticket__sbtn--buy" aria-pressed={side === "buy"} onClick={() => setSide("buy")}>Buy</button>
        <button type="button" className="mds-ticket__sbtn mds-ticket__sbtn--sell" aria-pressed={side === "sell"} onClick={() => setSide("sell")}>Sell</button>
      </div>
      <div className="mds-ticket__grid">
        <div className="mds-ticket__num">
          <Input label="Quantity" inputMode="decimal" value={qty} onChange={(e) => setQty(e.target.value)} placeholder="0" />
        </div>
        <Select label="Type" value={type} onChange={setType}
          options={[{ value: "market", label: "Market" }, { value: "limit", label: "Limit" }]} />
        <div className="mds-ticket__num">
          <Input label="Limit price" inputMode="decimal" value={limit} disabled={type !== "limit"}
            onChange={(e) => setLimit(e.target.value)} placeholder={type === "limit" ? "0.0000" : "—"} />
        </div>
        <Select label="Time in force" value={tif} onChange={setTif} options={tifOptions} />
      </div>
      <div className="mds-ticket__sum">
        <span className="mds-ticket__sum-label">Est. notional</span>
        <span>{notional != null ? fmtMoney(notional) : "—"}</span>
      </div>
      {live && (
        <label className="mds-ticket__gate">
          <input type="checkbox" checked={confirmLive} onChange={(e) => setConfirmLive(e.target.checked)} />
          <span>Live environment — this order executes against real capital. Confirm to enable submit.</span>
        </label>
      )}
      <Button variant="primary" disabled={!canSubmit} onClick={submit} style={{ width: "100%" }}>
        Submit {side} order
      </Button>
    </div>
  );
}
