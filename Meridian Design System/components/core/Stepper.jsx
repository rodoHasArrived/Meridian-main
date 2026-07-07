// Meridian stepper — tab-like multi-step form navigation. Jump to any step.
// No animation; instant step change. Shows step number + label + optional badge (status/count).
const Stepper = ({ steps, activeStep = 0, onStepChange, showStepNumber = true }) => {
  const css = `
.mds-stepper{display:flex;gap:1px;border-bottom:1px solid var(--border);background:var(--border);}
.mds-stepper-step{flex:1;appearance:none;border:none;background:var(--bg-light);cursor:pointer;
  font-family:var(--font-body);font-size:12px;color:var(--text-secondary);padding:11px 16px;
  text-align:left;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;transition:all .12s;}
.mds-stepper-step:hover{background:var(--bg-hover);color:var(--text-primary);}
.mds-stepper-step--active{background:var(--bg);color:var(--text-primary);font-weight:600;
  border-bottom:2px solid var(--accent);padding-bottom:9px;}
.mds-stepper-step--complete{color:var(--text-secondary);}
.mds-stepper-step__num{display:inline-block;width:20px;height:20px;border-radius:50%;
  background:var(--bg-medium);color:var(--text-muted);font-size:10px;font-weight:600;
  text-align:center;line-height:1.8;margin-right:8px;vertical-align:middle;}
.mds-stepper-step--active .mds-stepper-step__num{background:var(--accent);color:var(--text-on-accent,#fff);}
.mds-stepper-step--complete .mds-stepper-step__num{background:var(--green);color:var(--text-on-fill,#fff);font-size:11px;content:'✓';}
.mds-stepper-step__badge{display:inline-block;margin-left:8px;font-size:10px;
  background:var(--bg-medium);color:var(--text-muted);padding:2px 6px;border-radius:2px;}
`;
  if (!document.getElementById("mds-stepper-css")) {
    const el = document.createElement("style");
    el.id = "mds-stepper-css";
    el.textContent = css;
    document.head.appendChild(el);
  }

  return (
    <div className="mds-stepper" role="tablist">
      {steps.map((step, idx) => (
        <button key={idx} role="tab" aria-selected={idx === activeStep}
          className={`mds-stepper-step${idx === activeStep ? " mds-stepper-step--active" : ""}${idx < activeStep ? " mds-stepper-step--complete" : ""}`}
          onClick={() => onStepChange && onStepChange(idx)}>
          {showStepNumber && <span className="mds-stepper-step__num">{idx < activeStep ? "✓" : idx + 1}</span>}
          <span>{step.label}</span>
          {step.badge && <span className="mds-stepper-step__badge">{step.badge}</span>}
        </button>
      ))}
    </div>
  );
};

export { Stepper };
