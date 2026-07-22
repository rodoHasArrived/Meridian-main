// Meridian DatePicker — single date input with calendar, functional only.
import React from "react";
const { useState, useRef } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-date-wrap{position:relative;display:inline-block;width:100%;}
.mds-date-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.mds-date-input{width:100%;height:32px;padding:7px 10px;border:1px solid var(--border,#D7DCE2);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);font-family:var(--font-data);
  font-size:13px;cursor:pointer;}
.mds-date-input:focus{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-date-cal{position:absolute;top:calc(100% + 4px);left:0;z-index:100;background:var(--bg-light,#fff);
  border:1px solid var(--border,#D7DCE2);padding:12px;}
.mds-date-header{display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;
  font-family:var(--font-body);font-size:12px;font-weight:600;}
.mds-date-nav{cursor:pointer;padding:4px 8px;border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);}
.mds-date-nav:hover{background:var(--bg-hover,#F1F4F7);}
.mds-date-grid{display:grid;grid-template-columns:repeat(7,1fr);gap:2px;margin-bottom:8px;}
.mds-date-dow{font-family:var(--font-body);font-size:10px;font-weight:600;text-align:center;color:var(--text-muted,#59636F);}
.mds-date-day{width:32px;height:32px;display:flex;align-items:center;justify-content:center;
  border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);cursor:pointer;font-size:12px;}
.mds-date-day:hover{background:var(--bg-hover,#F1F4F7);}
.mds-date-day--other{color:var(--text-muted,#59636F);}
.mds-date-day--selected{background:var(--accent,#2F6F8F);color:var(--text-on-accent,#fff);border-color:var(--accent,#2F6F8F);}
.mds-date-day--today{border:2px solid var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "datepicker");
  el.textContent = css;
  document.head.appendChild(el);
}

export function DatePicker({ label, value, onChange }) {
  inject();
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState(value ? new Date(value) : new Date());
  const ref = useRef(null);

  React.useEffect(() => {
    const handler = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const firstDay = new Date(month.getFullYear(), month.getMonth(), 1);
  const lastDay = new Date(month.getFullYear(), month.getMonth() + 1, 0);
  const startDate = new Date(firstDay);
  startDate.setDate(startDate.getDate() - firstDay.getDay());

  const days = [];
  for (let i = 0; i < 42; i++) {
    const d = new Date(startDate);
    d.setDate(d.getDate() + i);
    days.push(d);
  }

  const formatDate = (d) => d.toISOString().split('T')[0];
  const displayDate = value ? new Date(value).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' }) : 'Select date…';

  return (
    <div ref={ref}>
      {label && <label className="mds-date-label">{label}</label>}
      <div className="mds-date-wrap">
        <input
          type="text"
          className="mds-date-input"
          value={displayDate}
          readOnly
          onClick={() => setOpen(!open)}
        />
        {open && (
          <div className="mds-date-cal">
            <div className="mds-date-header">
              <button className="mds-date-nav" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1))}>←</button>
              <span>{month.toLocaleDateString('en-US', { year: 'numeric', month: 'long' })}</span>
              <button className="mds-date-nav" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1))}>→</button>
            </div>
            <div className="mds-date-grid">
              {['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'].map(d => <div key={d} className="mds-date-dow">{d}</div>)}
              {days.map((d, i) => {
                const isOther = d.getMonth() !== month.getMonth();
                const isSelected = value && formatDate(d) === value;
                const isToday = formatDate(d) === formatDate(new Date());
                return (
                  <button
                    key={i}
                    className={`mds-date-day${isOther ? " mds-date-day--other" : ""}${isSelected ? " mds-date-day--selected" : ""}${isToday && !isSelected ? " mds-date-day--today" : ""}`}
                    onClick={() => { onChange?.(formatDate(d)); setOpen(false); }}
                  >
                    {d.getDate()}
                  </button>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
