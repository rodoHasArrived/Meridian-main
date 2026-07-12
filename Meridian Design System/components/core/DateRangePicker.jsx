// Meridian DateRangePicker — start/end date selection, functional only.
import React from "react";
const { useState, useRef } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-drange-wrap{position:relative;display:inline-block;width:100%;}
.mds-drange-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.mds-drange-input{width:100%;height:32px;padding:7px 10px;border:1px solid var(--border,#D7DCE2);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);font-family:var(--font-data);
  font-size:13px;cursor:pointer;}
.mds-drange-input:focus{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-drange-cal{position:absolute;top:calc(100% + 4px);left:0;z-index:100;background:var(--bg-light,#fff);
  border:1px solid var(--border,#D7DCE2);padding:12px;display:grid;grid-template-columns:1fr 1fr;gap:16px;}
.mds-drange-month{width:280px;}
.mds-drange-header{display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;
  font-family:var(--font-body);font-size:12px;font-weight:600;}
.mds-drange-nav{cursor:pointer;padding:4px 8px;border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);}
.mds-drange-nav:hover{background:var(--bg-hover,#F1F4F7);}
.mds-drange-grid{display:grid;grid-template-columns:repeat(7,1fr);gap:2px;}
.mds-drange-dow{font-family:var(--font-body);font-size:10px;font-weight:600;text-align:center;color:var(--text-muted,#59636F);}
.mds-drange-day{width:32px;height:32px;display:flex;align-items:center;justify-content:center;
  border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);cursor:pointer;font-size:12px;}
.mds-drange-day:hover{background:var(--bg-hover,#F1F4F7);}
.mds-drange-day--inrange{background:var(--blue-a10,rgba(47,111,143,.10));}
.mds-drange-day--start,.mds-drange-day--end{background:var(--accent,#2F6F8F);color:var(--text-on-accent,#fff);border-color:var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "daterangepicker");
  el.textContent = css;
  document.head.appendChild(el);
}

export function DateRangePicker({ label, start, end, onChange }) {
  inject();
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState(new Date());
  const ref = useRef(null);

  React.useEffect(() => {
    const handler = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const formatDate = (d) => d ? d.toISOString().split('T')[0] : null;
  const displayStart = start ? new Date(start).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : 'Start';
  const displayEnd = end ? new Date(end).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : 'End';

  const getDaysInMonth = (date) => {
    const firstDay = new Date(date.getFullYear(), date.getMonth(), 1);
    const lastDay = new Date(date.getFullYear(), date.getMonth() + 1, 0);
    const startDate = new Date(firstDay);
    startDate.setDate(startDate.getDate() - firstDay.getDay());
    const days = [];
    for (let i = 0; i < 42; i++) {
      const d = new Date(startDate);
      d.setDate(d.getDate() + i);
      days.push(d);
    }
    return days;
  };

  const handleDayClick = (d) => {
    const dateStr = formatDate(d);
    if (!start || (start && end)) {
      onChange({ start: dateStr, end: null });
    } else if (dateStr < start) {
      onChange({ start: dateStr, end: start });
    } else {
      onChange({ start, end: dateStr });
      setOpen(false);
    }
  };

  const isInRange = (d) => {
    const dateStr = formatDate(d);
    if (!start || !end) return false;
    return dateStr >= start && dateStr <= end;
  };

  const days1 = getDaysInMonth(month);
  const days2 = getDaysInMonth(new Date(month.getFullYear(), month.getMonth() + 1));

  return (
    <div ref={ref}>
      {label && <label className="mds-drange-label">{label}</label>}
      <div className="mds-drange-wrap">
        <input
          type="text"
          className="mds-drange-input"
          value={`${displayStart} — ${displayEnd}`}
          readOnly
          onClick={() => setOpen(!open)}
        />
        {open && (
          <div className="mds-drange-cal">
            {[days1, days2].map((days, idx) => {
              const m = new Date(month);
              m.setMonth(m.getMonth() + idx);
              return (
                <div key={idx} className="mds-drange-month">
                  <div className="mds-drange-header">
                    {idx === 0 && <button className="mds-drange-nav" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1))}>←</button>}
                    {idx === 0 && <span></span>}
                    <span>{m.toLocaleDateString('en-US', { year: 'numeric', month: 'long' })}</span>
                    {idx === 1 && <button className="mds-drange-nav" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1))}>→</button>}
                    {idx === 1 && <span></span>}
                  </div>
                  <div className="mds-drange-grid">
                    {['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'].map(d => <div key={d} className="mds-drange-dow">{d}</div>)}
                    {days.map((d, i) => {
                      const dateStr = formatDate(d);
                      const isStart = start && dateStr === start;
                      const isEnd = end && dateStr === end;
                      const inRange = isInRange(d);
                      return (
                        <button
                          key={i}
                          className={`mds-drange-day${inRange ? " mds-drange-day--inrange" : ""}${isStart ? " mds-drange-day--start" : ""}${isEnd ? " mds-drange-day--end" : ""}`}
                          onClick={() => handleDayClick(d)}
                        >
                          {d.getDate()}
                        </button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
