// Meridian basis bridge — one economic event, several accounting bases.
// Statutory, GAAP, tax, and management treatments of the same trade sit in
// aligned columns so a change to one basis reads as a change to one basis. Rows
// that differ from the reference basis are marked structurally (rule + marker),
// not by color alone.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-bb{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-body);font-size:12px;
  color:var(--text-secondary);}
.mds-bb th,.mds-bb td{padding:6px 10px;text-align:left;vertical-align:top;
  border-bottom:1px solid var(--border-divider);}
.mds-bb thead th{background:var(--bg-medium);font-size:var(--type-label,10px);
  font-weight:var(--weight-semibold,600);font-variant:all-small-caps;letter-spacing:.05em;
  color:var(--text-muted);border-bottom:1px solid var(--border-strong);white-space:nowrap;}
.mds-bb thead th span{display:block;font-family:var(--font-data);font-size:10px;font-variant:normal;
  letter-spacing:0;color:var(--text-muted);font-weight:400;text-transform:none;}
.mds-bb tbody th{font-weight:var(--weight-medium,500);color:var(--text-secondary);white-space:nowrap;}
.mds-bb td{font-family:var(--font-data);color:var(--text-primary);font-variant-numeric:tabular-nums;}
.mds-bb td.is-ref{color:var(--text-secondary);}
.mds-bb td.is-changed{background:var(--cond-overridden-bg);box-shadow:inset 2px 0 0 var(--cond-overridden-fg);
  color:var(--cond-overridden-fg);font-weight:var(--weight-medium,500);}
.mds-bb td.is-changed::after{content:" ◆";font-size:8px;vertical-align:2px;}
.mds-bb tfoot td,.mds-bb tfoot th{border-bottom:0;border-top:1px solid var(--border-strong);
  background:var(--bg-light);font-family:var(--font-body);}
.mds-bb__wrap{width:100%;overflow:auto;border:1px solid var(--border);border-radius:var(--radius-chip,2px);
  background:var(--bg-light);}
.mds-bb__flag{display:inline-flex;align-items:center;gap:5px;font-size:11px;}
.mds-bb__flag i{font-style:normal;font-size:8px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "basis-bridge");
  el.textContent = css;
  document.head.appendChild(el);
}

export function BasisBridge({
  bases = [], rows = [], attributeLabel = "Treatment", footer, className = "", ...rest
}) {
  inject();
  return (
    <div className={`mds-bb__wrap${className ? " " + className : ""}`} {...rest}>
      <table className="mds-bb">
        <thead>
          <tr>
            <th scope="col">{attributeLabel}</th>
            {bases.map((b) => (
              <th key={b.id} scope="col">
                {b.label}
                {b.meta ? <span>{b.meta}</span> : null}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={r.id || r.label || i}>
              <th scope="row">{r.label}</th>
              {bases.map((b) => {
                const changed = Array.isArray(r.changed) && r.changed.includes(b.id);
                const v = r.values ? r.values[b.id] : undefined;
                return (
                  <td key={b.id} className={changed ? "is-changed" : b.reference ? "is-ref" : ""}
                    title={changed ? `Differs from the configured ${b.label} treatment` : undefined}>
                    {v === undefined || v === null || v === "" ? "—" : v}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
        {footer && footer.length ? (
          <tfoot>
            <tr>
              <th scope="row">{footer[0].label}</th>
              {bases.map((b) => {
                const f = footer[0].values ? footer[0].values[b.id] : undefined;
                const tone = footer[0].tones ? footer[0].tones[b.id] : undefined;
                return (
                  <td key={b.id} style={tone ? { color: `var(--cond-${tone}-fg)` } : undefined}>
                    <span className="mds-bb__flag">{f || "—"}</span>
                  </td>
                );
              })}
            </tr>
          </tfoot>
        ) : null}
      </table>
    </div>
  );
}
