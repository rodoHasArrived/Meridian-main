// Meridian strategy-onboarding — template screen. Mounted by the DC via <x-import>; reads design-system
// components from the compiled bundle.

const {
  WorkstationTopbar, NavRail, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  Stepper, ProgressBar, Input, TextArea, Select, SegmentedControl, RadioGroup,
  Callout, KeyValueGrid
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const NAV = [
  { label: "Strategy", items: [
    { id: "builder", label: "Strategy Builder", icon: "../../assets/icons/strategy-builder.svg", shortcut: "G S" },
    { id: "fields", label: "Field & Formula", icon: "../../assets/icons/formula.svg" },
    { id: "backtest", label: "Backtest", icon: "../../assets/icons/backtest.svg", shortcut: "G B" },
  ]},
  { label: "Data & Review", items: [
    { id: "catalog", label: "AMX Catalog", icon: "../../assets/icons/data-sources.svg" },
    { id: "governance", label: "Governance", icon: "../../assets/icons/governance.svg" },
    { id: "runs", label: "Strategy Runs", icon: "../../assets/icons/strategy-runs.svg" },
  ]},
];

const ROUTES = {
  builder: "../strategy-builder/index.html", fields: "../field-formula/index.html", backtest: "../backtest-builder/index.html",
  catalog: "../amx-governance/index.html", governance: "../amx-governance/index.html", runs: "../strategy-runs/index.html",
};

const STEPS = [
  { label: "Name & purpose", badge: "1" },
  { label: "Universe & data", badge: "2" },
  { label: "First cell", badge: "3" },
  { label: "Review & run", badge: "4" },
];

const FIELD_CHIPS = ["PRICE", "YTM", "DURATION", "RATING", "SPREAD_TSY", "COUPON", "CLEAN_PRICE", "CONVEXITY"];

function StrategyOnboardingScreen() {
  const [step, setStep] = useState(0);
  const [name, setName] = useState("Bond Carry & Roll");
  const [asset, setAsset] = useState("fixed-income");
  const [ds, setDs] = useState("hy-corp");
  const [mode, setMode] = useState("formula");
  const [tpl, setTpl] = useState("current-yield");
  const [chips, setChips] = useState(new Set(["PRICE", "YTM", "DURATION", "RATING"]));
  const toggleChip = (c) => setChips((p) => { const n = new Set(p); n.has(c) ? n.delete(c) : n.add(c); return n; });

  const TPL_CODE = {
    "current-yield": "Let current_yield = COUPON(cusip) / PRICE(cusip)\nFilter current_yield >= 0.05",
    "ytm-screen": 'Let y = YTM(cusip)\nFilter y >= min_yield And RATING(cusip) >= "BBB"',
    "duration-band": "Filter DURATION(cusip) >= 3 And DURATION(cusip) <= 8",
  };

  const stepBody = () => {
    if (step === 0) return (
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <Input label="Strategy name" value={name} onChange={(e) => setName(e.target.value)} />
        <TextArea label="Description" rows={3} defaultValue="Long investment-grade and crossover credit on carry, trimming when modified duration runs long." />
        <div>
          <div style={{ font: "600 10px var(--font-body)", letterSpacing: ".03em", fontVariant: "all-small-caps", color: "var(--text-muted)", marginBottom: 6 }}>Asset class</div>
          <RadioGroup orientation="horizontal" value={asset} onChange={setAsset} options={[
            { value: "fixed-income", label: "Fixed income" },
            { value: "equity", label: "Equity" },
            { value: "multi-asset", label: "Multi-asset" },
          ]} />
        </div>
      </div>
    );
    if (step === 1) return (
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <Select label="Backtest dataset" value={ds} onChange={setDs} options={[
          { value: "hy-corp", label: "HY Corp Bonds — 1,284 sessions" },
          { value: "ig-corp", label: "IG Corp Bonds — 1,284 sessions" },
          { value: "util", label: "Utility Sector — 988 sessions" },
        ]} />
        <div>
          <div style={{ font: "600 10px var(--font-body)", letterSpacing: ".03em", fontVariant: "all-small-caps", color: "var(--text-muted)", marginBottom: 8 }}>AMX fields in scope — {chips.size} selected</div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {FIELD_CHIPS.map((c) => {
              const on = chips.has(c);
              return (
                <button key={c} onClick={() => toggleChip(c)} style={{ cursor: "pointer", font: "12px var(--font-data)", padding: "5px 10px",
                  border: "1px solid " + (on ? "var(--accent)" : "var(--border)"), borderRadius: 2,
                  background: on ? "color-mix(in srgb, var(--accent) 12%, transparent)" : "var(--card-surface)",
                  color: on ? "var(--accent)" : "var(--text-secondary)" }}>{on ? "✓ " : "+ "}{c}</button>
              );
            })}
          </div>
        </div>
        <Callout tone="info" title="Lineage tracked">Every field carries its AMX lineage and a documented fallback — visible in the catalog and the run trace.</Callout>
      </div>
    );
    if (step === 2) return (
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <div>
          <div style={{ font: "600 10px var(--font-body)", letterSpacing: ".03em", fontVariant: "all-small-caps", color: "var(--text-muted)", marginBottom: 6 }}>Cell mode</div>
          <SegmentedControl value={mode} onChange={setMode} options={[
            { value: "visual", label: "Visual" }, { value: "formula", label: "Formula" }, { value: "code", label: "Code" }]} />
        </div>
        <Select label="Starter template" value={tpl} onChange={setTpl} options={[
          { value: "current-yield", label: "Current yield screen" },
          { value: "ytm-screen", label: "YTM + rating screen" },
          { value: "duration-band", label: "Duration band filter" },
        ]} />
        <div>
          <Eyebrow>Cell preview</Eyebrow>
          <pre style={{ margin: "6px 0 0", padding: 12, fontSize: 13, fontFamily: "var(--font-data)", lineHeight: 1.7,
            background: "var(--bg-medium)", border: "1px solid var(--border)", color: "var(--text-primary)", whiteSpace: "pre-wrap" }}>{TPL_CODE[tpl]}</pre>
        </div>
      </div>
    );
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <KeyValueGrid columns={2} items={[
          { label: "Name", value: name },
          { label: "Asset class", value: asset },
          { label: "Dataset", value: ds === "hy-corp" ? "HY Corp Bonds" : ds },
          { label: "Fields", value: Array.from(chips).join(" · ") || "none" },
          { label: "First cell", value: mode + " · " + tpl },
          { label: "Environment", value: "Fixture replay" },
        ]} />
        <Callout tone="success" title="Ready to run">First backtest runs against fixture data — no live capital. Approval is gated until a backtest proof exists.</Callout>
      </div>
    );
  };

  return (
    <React.Fragment>
      <WorkstationTopbar moduleLabel="New Strategy" environment="FIXTURE" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <NavRail activeId="builder" sections={NAV} onSelect={(id) => { const r = ROUTES[id]; if (r) window.location.href = r; }} />
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 24, display: "flex", justifyContent: "center" }}>
          <div style={{ width: "100%", maxWidth: 760, display: "flex", flexDirection: "column", gap: 16 }}>

            <div>
              <Eyebrow>New strategy</Eyebrow>
              <h1 style={{ font: "600 24px var(--font-display)", margin: "2px 0 0", color: "var(--text-primary)" }}>Set up your strategy</h1>
            </div>

            <Stepper steps={STEPS} activeStep={step} onStepChange={setStep} showStepNumber={false} />
            <ProgressBar value={Math.round(((step + 1) / STEPS.length) * 100)} showValue label={"Step " + (step + 1) + " of " + STEPS.length} />

            <PanelSurface raised style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
              <div>
                <Eyebrow>{STEPS[step].label}</Eyebrow>
              </div>
              {stepBody()}
            </PanelSurface>

            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <Button variant="ghost" size="sm" disabled={step === 0} onClick={() => setStep((s) => Math.max(0, s - 1))}>Back</Button>
              <div style={{ flex: 1 }}></div>
              <Button variant="link" size="sm">Skip setup</Button>
              {step < STEPS.length - 1
                ? <Button variant="primary" size="sm" onClick={() => setStep((s) => Math.min(STEPS.length - 1, s + 1))}>Continue</Button>
                : <Button variant="primary" size="sm">Create &amp; run backtest</Button>}
            </div>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Setup", value: STEPS[step].label },
        { label: "Progress", value: Math.round(((step + 1) / STEPS.length) * 100) + "%" },
        { status: "ok", label: "Env", value: "Fixture", push: true },
      ]} />
    </React.Fragment>
  );
}

window.StrategyOnboardingScreen = StrategyOnboardingScreen;
if (typeof module !== "undefined") module.exports = { StrategyOnboardingScreen };
