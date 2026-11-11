// Meridian Stepper — ordered multi-step form navigation (onboarding, reconciliation wizards,
// trade capture). Instant step change, no animation.
// Progress semantics: an ordered list of steps, NOT tabs. Each step announces its position and
// state ("Step 2 of 4, current"); the current step carries aria-current="step".
// When no onStepChange is given the steps render as static text, not dead buttons.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-stepper{display:flex;gap:1px;background:var(--border);list-style:none;margin:0;padding:0;}
.mds-stepper--h{border-bottom:1px solid var(--border);}
.mds-stepper--v{flex-direction:column;border-right:1px solid var(--border);}
.mds-stepper>li{display:flex;min-width:0;}
.mds-stepper--h>li{flex:1;}
.mds-stepper-step{flex:1;width:100%;box-sizing:border-box;appearance:none;border:none;margin:0;
  background:var(--bg-light);font-family:var(--font-body);font-size:12px;color:var(--text-secondary);
  padding:0 16px;min-height:var(--stepper-height,var(--theme-row-height,36px));text-align:left;
  display:flex;align-items:center;gap:8px;}
button.mds-stepper-step{cursor:pointer;}
button.mds-stepper-step:hover{background:var(--bg-hover);color:var(--text-primary);}
.mds-stepper-step:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-stepper-step--active{background:var(--bg);color:var(--text-primary);font-weight:600;}
.mds-stepper--h .mds-stepper-step--active{box-shadow:inset 0 -2px 0 var(--brand-signal,#C56343);}
.mds-stepper--v .mds-stepper-step--active{box-shadow:inset 2px 0 0 var(--brand-signal,#C56343);}
.mds-stepper-step--disabled{color:var(--text-disabled,#A4ABB2);cursor:not-allowed;background:var(--bg-light);}
button.mds-stepper-step--disabled:hover{background:var(--bg-light);color:var(--text-disabled,#A4ABB2);}
.mds-stepper-step__body{min-width:0;display:flex;flex-direction:column;gap:1px;}
.mds-stepper-step__label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-stepper-step__desc{font-size:10px;font-weight:400;color:var(--text-muted);
  overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-stepper-step__num{flex:0 0 auto;display:inline-flex;align-items:center;justify-content:center;
  width:20px;height:20px;border-radius:var(--radius-pill,999px);
  background:var(--bg-medium);color:var(--text-muted);font-size:10px;font-weight:600;
  font-variant-numeric:tabular-nums;}
.mds-stepper-step--active .mds-stepper-step__num{background:var(--interaction-selected,#496C8F);color:var(--text-on-accent,#0D1217);}
.mds-stepper-step--complete .mds-stepper-step__num{background:var(--green);color:var(--text-on-fill,#fff);font-size:11px;}
.mds-stepper-step--error .mds-stepper-step__num{background:var(--red,#B54B42);color:var(--text-on-fill,#fff);}
.mds-stepper-step--error .mds-stepper-step__label{color:var(--red-dim,#883832);}
.mds-stepper-step__badge{flex:0 0 auto;margin-left:auto;font-size:10px;font-variant-numeric:tabular-nums;
  background:var(--bg-medium);color:var(--text-muted);padding:2px 6px;border-radius:var(--radius-chip,2px);}
.mds-stepper-step--error .mds-stepper-step__badge{background:var(--red-a10,rgba(181, 75, 66, 0.1));color:var(--red-dim,#883832);}
.mds-stepper-step__sr{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;border:0;}
@media print{.mds-stepper{background:none;}.mds-stepper-step{background:none;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "stepper");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Stepper({
  steps = [],
  activeStep = 0,
  onStepChange,
  showStepNumber = true,
  orientation = "horizontal",
  label = "Steps",
  ...rest
}) {
  inject();
  const interactive = typeof onStepChange === "function";
  return (
    <ol className={`mds-stepper mds-stepper--${orientation === "vertical" ? "v" : "h"}`} aria-label={label} {...rest}>
      {steps.map((step, idx) => {
        const complete = step.complete != null ? step.complete : idx < activeStep;
        const current = idx === activeStep;
        const state = step.error ? "has errors" : complete ? "completed" : current ? "current" : "not started";
        // A step is reachable unless explicitly disabled; `locked` blocks steps not yet unlocked.
        const blocked = !!step.disabled || (!!step.locked && !complete && !current);
        const cls = [
          "mds-stepper-step",
          current && "mds-stepper-step--active",
          complete && !current && "mds-stepper-step--complete",
          step.error && "mds-stepper-step--error",
          blocked && "mds-stepper-step--disabled",
        ].filter(Boolean).join(" ");
        const inner = (
          <>
            {showStepNumber && (
              <span className="mds-stepper-step__num" aria-hidden="true">
                {step.error ? "!" : complete && !current ? "✓" : idx + 1}
              </span>
            )}
            <span className="mds-stepper-step__sr">{`Step ${idx + 1} of ${steps.length}, ${state}: `}</span>
            <span className="mds-stepper-step__body">
              <span className="mds-stepper-step__label">{step.label}</span>
              {step.description && <span className="mds-stepper-step__desc">{step.description}</span>}
            </span>
            {step.badge != null && <span className="mds-stepper-step__badge">{step.badge}</span>}
          </>
        );
        return (
          <li key={step.id ?? idx}>
            {interactive ? (
              <button
                type="button"
                className={cls}
                aria-current={current ? "step" : undefined}
                disabled={blocked}
                onClick={() => onStepChange(idx)}
              >{inner}</button>
            ) : (
              <span className={cls} aria-current={current ? "step" : undefined}>{inner}</span>
            )}
          </li>
        );
      })}
    </ol>
  );
}

Stepper.displayName = "Stepper";
