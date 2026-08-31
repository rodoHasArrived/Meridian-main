// Meridian InstrumentChip — the identity micro-chip for a tradable instrument: mono symbol,
// small-caps venue, and an asset-class block letter on a washed semantic tint. Use it wherever
// a symbol appears outside a table cell — watchlists, tickets, inspectors, chips-in-copy.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ichip{display:inline-flex;align-items:center;gap:6px;border:1px solid var(--border,#CBD3DC);
  background:var(--bg-light,#FFFFFF);border-radius:var(--radius-chip,2px);padding:2px 7px 2px 3px;
  font-family:var(--font-data,'Cascadia Mono',monospace);line-height:1.4;white-space:nowrap;color:var(--text-primary,#22272E);}
button.mds-ichip{cursor:pointer;}
button.mds-ichip:hover{border-color:var(--border-hover,#ADB8C4);background:var(--bg-hover,#EAEEF3);}
button.mds-ichip:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-ichip--selected{border-color:var(--accent,#2F6F8F);background:var(--blue-a10,rgba(47,111,143,.1));color:var(--text-primary,#22272E);}
.mds-ichip__class{display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;
  font-size:8.5px;font-weight:700;letter-spacing:.02em;border:1px solid;border-radius:var(--radius-chip,2px);flex:0 0 auto;}
.mds-ichip__sym{font-size:12px;font-weight:700;letter-spacing:.01em;color:var(--text-primary,#22272E);}
.mds-ichip__venue{font-family:var(--font-body);font-size:9.5px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.05em;color:var(--text-muted,#59636F);}
.mds-ichip--sm{padding:1px 5px 1px 2px;gap:5px;}
.mds-ichip--sm .mds-ichip__class{width:13px;height:13px;font-size:7.5px;}
.mds-ichip--sm .mds-ichip__sym{font-size:11px;}
.mds-ichip__class--eq{color:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);background:var(--blue-a10,rgba(47,111,143,.1));}
.mds-ichip__class--fut{color:var(--purple-dim,#55467E);border-color:var(--purple,#6F5BA7);background:var(--purple-a10,rgba(111,91,167,.1));}
.mds-ichip__class--opt{color:var(--orange-dim,#683E0B);border-color:var(--orange,#8A520E);background:var(--orange-a10,rgba(138,82,14,.1));}
.mds-ichip__class--fx{color:var(--green-dim,#10663F);border-color:var(--green,#16885F);background:var(--green-a10,rgba(22,136,95,.1));}
.mds-ichip__class--cr{color:var(--red-dim,#8C2F40);border-color:var(--red,#BA3F55);background:var(--red-a10,rgba(186,63,85,.1));}
.mds-ichip__class--bd{color:var(--text-secondary,#4D5967);border-color:var(--border-strong,#99A5B2);background:var(--bg-hover,#EAEEF3);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "instrument-chip");
  el.textContent = css;
  document.head.appendChild(el);
}

const CLASS_MAP = {
  eq: { code: "eq", letter: "E", label: "Equity" },
  equity: { code: "eq", letter: "E", label: "Equity" },
  etf: { code: "eq", letter: "F", label: "ETF" },
  fut: { code: "fut", letter: "U", label: "Future" },
  future: { code: "fut", letter: "U", label: "Future" },
  opt: { code: "opt", letter: "O", label: "Option" },
  option: { code: "opt", letter: "O", label: "Option" },
  fx: { code: "fx", letter: "X", label: "FX" },
  crypto: { code: "cr", letter: "C", label: "Crypto" },
  cr: { code: "cr", letter: "C", label: "Crypto" },
  bond: { code: "bd", letter: "B", label: "Bond" },
  bd: { code: "bd", letter: "B", label: "Bond" },
};

export function InstrumentChip({
  symbol,
  venue,
  assetClass = "eq",
  size = "md",
  selected = false,
  onClick,
  className = "",
  ...rest
}) {
  inject();
  const cls = CLASS_MAP[String(assetClass).toLowerCase()] || CLASS_MAP.eq;
  const Tag = onClick ? "button" : "span";
  return (
    <Tag
      type={onClick ? "button" : undefined}
      onClick={onClick}
      title={`${symbol}${venue ? " · " + venue : ""} · ${cls.label}`}
      className={`mds-ichip mds-ichip--${size}${selected ? " mds-ichip--selected" : ""}${className ? " " + className : ""}`}
      {...rest}
    >
      <span className={`mds-ichip__class mds-ichip__class--${cls.code}`} aria-hidden="true">{cls.letter}</span>
      <span className="mds-ichip__sym">{symbol}</span>
      {venue && <span className="mds-ichip__venue">{venue}</span>}
    </Tag>
  );
}

InstrumentChip.displayName = "InstrumentChip";
