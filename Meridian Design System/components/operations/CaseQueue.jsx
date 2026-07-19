// Meridian case queue — operator triage list mirroring the server case models
// (`ReconciliationCaseSummaryDto`, `OperationsBreakCaseDto`): id · summary · category ·
// status · priority · assignee · SLA. Priority renders as a 3px left rail; status as a
// SeverityBadge; SLA as an SlaChip. Selection follows the listbox pattern (click,
// ArrowUp/Down + Enter). The queue owns no state — pass `selectedId` + `onSelect`.
import React, { useRef } from "react";
import { SeverityBadge } from "./SeverityBadge";
import { SlaChip } from "./SlaChip";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-caseq{display:flex;flex-direction:column;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-card,6px);background:var(--bg-light,#FFFFFF);overflow:hidden;}
.mds-caseq__row{display:grid;grid-template-columns:3px auto 1fr auto;align-items:center;gap:10px;
  padding:0 12px 0 0;min-height:var(--theme-row-height,32px);border-bottom:1px solid var(--border,#E4E8EC);
  background:transparent;border-left:0;border-right:0;border-top:0;text-align:left;cursor:pointer;
  font-family:var(--font-body,"Segoe UI Variable Text",sans-serif);}
.mds-caseq__row:last-child{border-bottom:0;}
.mds-caseq__row:hover{background:var(--bg-hover,#F0F2F5);}
.mds-caseq__row:focus-visible{outline:2px solid var(--focus-ring,#2F6F8F);outline-offset:-2px;}
.mds-caseq__row[aria-selected="true"]{background:var(--accent-a10,rgba(47,111,143,.08));}
.mds-caseq__rail{align-self:stretch;background:transparent;}
.mds-caseq__rail--critical{background:var(--severity-blocked-fg,#BA3F55);}
.mds-caseq__rail--high{background:var(--severity-action-fg,#8A520E);}
.mds-caseq__id{font-family:var(--font-data,"Cascadia Mono",monospace);font-size:10.5px;font-weight:700;
  color:var(--text-muted,#6E7781);padding-left:9px;white-space:nowrap;}
.mds-caseq__mid{display:flex;flex-direction:column;gap:2px;min-width:0;padding:6px 0;}
.mds-caseq__summary{font-size:12.5px;font-weight:600;color:var(--text-primary,#1A2027);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-caseq__sub{display:flex;align-items:center;gap:6px;font-size:10.5px;color:var(--text-muted,#6E7781);
  font-family:var(--font-data,"Cascadia Mono",monospace);white-space:nowrap;overflow:hidden;}
.mds-caseq__end{display:flex;align-items:center;gap:8px;flex:0 0 auto;}
.mds-caseq__who{font-size:11px;color:var(--text-secondary,#3D454F);white-space:nowrap;}
.mds-caseq__who--none{color:var(--severity-action-fg,#8A520E);font-style:italic;}
.mds-caseq__empty{padding:18px 14px;font-size:12px;color:var(--text-muted,#6E7781);text-align:center;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "case-queue");
  el.textContent = css;
  document.head.appendChild(el);
}

function railClass(priority) {
  const p = String(priority || "").toLowerCase();
  if (p === "critical" || p === "urgent") return "mds-caseq__rail--critical";
  if (p === "high") return "mds-caseq__rail--high";
  return "";
}

export function CaseQueue({
  items = [],
  selectedId,
  onSelect,
  now,
  emptyLabel = "No open cases",
  className = "",
  ...rest
}) {
  inject();
  const listRef = useRef(null);

  const move = (dir) => {
    const idx = items.findIndex((it) => it.id === selectedId);
    const next = Math.min(items.length - 1, Math.max(0, (idx < 0 ? 0 : idx + dir)));
    const item = items[next];
    if (item && onSelect) onSelect(item.id, item);
  };
  const onKeyDown = (e) => {
    if (e.key === "ArrowDown") { e.preventDefault(); move(1); }
    else if (e.key === "ArrowUp") { e.preventDefault(); move(-1); }
  };

  if (!items.length) {
    return (
      <div className={`mds-caseq${className ? " " + className : ""}`} {...rest}>
        <div className="mds-caseq__empty">{emptyLabel}</div>
      </div>
    );
  }

  return (
    <div
      className={`mds-caseq${className ? " " + className : ""}`}
      role="listbox"
      aria-label="Case queue"
      ref={listRef}
      onKeyDown={onKeyDown}
      {...rest}
    >
      {items.map((it) => {
        const selected = it.id === selectedId;
        const subParts = [it.category, it.reason, it.openedAtUtc && `opened ${it.openedAtUtc}`]
          .filter(Boolean);
        return (
          <button
            key={it.id}
            type="button"
            role="option"
            aria-selected={selected}
            className="mds-caseq__row"
            tabIndex={selected || (!selectedId && it === items[0]) ? 0 : -1}
            onClick={() => onSelect && onSelect(it.id, it)}
          >
            <span className={`mds-caseq__rail ${railClass(it.priority)}`} aria-hidden="true" />
            <span className="mds-caseq__id">{it.id}</span>
            <span className="mds-caseq__mid">
              <span className="mds-caseq__summary">{it.summary}</span>
              {subParts.length ? <span className="mds-caseq__sub">{subParts.join(" · ")}</span> : null}
            </span>
            <span className="mds-caseq__end">
              {it.sla ? <SlaChip now={now} {...it.sla} /> : null}
              {it.status ? <SeverityBadge status={it.status} /> : null}
              <span className={`mds-caseq__who${it.assignee ? "" : " mds-caseq__who--none"}`}>
                {it.assignee || "Unassigned"}
              </span>
            </span>
          </button>
        );
      })}
    </div>
  );
}
