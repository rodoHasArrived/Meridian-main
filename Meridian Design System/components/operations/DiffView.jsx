// Meridian DiffView — before → after field changes for review/audit rails (AMX governance,
// config approvals, alert-rule edits). Terse rows: small-caps field label, struck "before"
// in a red wash, arrow, "after" in a green wash. Values are mono — they're data.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-diff{border:1px solid var(--border,#CBD3DC);background:var(--bg-light,#fff);font-family:var(--font-body);}
.mds-diff__row{display:grid;grid-template-columns:minmax(110px,170px) 1fr 18px 1fr;gap:8px;align-items:center;
  padding:7px 12px;border-top:1px solid var(--border-divider,#D2D9E2);}
.mds-diff__row:first-child{border-top:none;}
.mds-diff__field{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#59636F);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-diff__val{font-family:var(--font-data);font-size:12px;padding:2px 6px;min-width:0;
  overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-variant-numeric:tabular-nums;}
.mds-diff__before{background:var(--red-a10,rgba(186,63,85,.10));color:var(--red-dim,#8C2F40);text-decoration:line-through;}
.mds-diff__after{background:var(--green-a10,rgba(22,136,95,.10));color:var(--green-dim,#10663F);}
.mds-diff__row--same .mds-diff__val{background:transparent;color:var(--text-secondary,#4D5967);text-decoration:none;}
.mds-diff__arrow{color:var(--text-muted,#59636F);text-align:center;font-size:11px;}
.mds-diff__empty{padding:14px 12px;font-size:12px;color:var(--text-muted,#59636F);text-align:center;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "diffview");
  el.textContent = css;
  document.head.appendChild(el);
}

const show = (v) => (v === null || v === undefined || v === "" ? "—" : String(v));

export function DiffView({ changes = [], emptyLabel = "No changes", className = "", ...rest }) {
  inject();
  return (
    <div className={`mds-diff${className ? " " + className : ""}`} {...rest}>
      {changes.length === 0 && <div className="mds-diff__empty">{emptyLabel}</div>}
      {changes.map((c, i) => {
        const same = show(c.before) === show(c.after);
        return (
          <div key={c.field + i} className={`mds-diff__row${same ? " mds-diff__row--same" : ""}`}>
            <span className="mds-diff__field">{c.field}</span>
            <span className="mds-diff__val mds-diff__before" title={show(c.before)}>{show(c.before)}</span>
            <span className="mds-diff__arrow" aria-hidden="true">→</span>
            <span className="mds-diff__val mds-diff__after" title={show(c.after)}>{show(c.after)}</span>
          </div>
        );
      })}
    </div>
  );
}
