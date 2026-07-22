// Meridian session-start — template screen. The missing lifecycle surface: credentials →
// MFA verify → environment gate (Live / Paper / Fixture with the live typed confirm) → role.
// Mounted by the DC via <x-import>; reads components from the compiled bundle.

const {
  PanelSurface, Input, Button, Badge, Stepper, Select, StatusBar, StatusBanner,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const ENVS = [
  { id: "LIVE", name: "Live", variant: "live", mode: "--mode-live", bg: "--state-live-bg", bd: "--state-live-bd",
    desc: "Real money. Orders route to brokers; fills are binding." },
  { id: "PAPER", name: "Paper", variant: "paper", mode: "--mode-paper", bg: "--state-paper-bg", bd: "--state-paper-bd",
    desc: "Simulated fills against live market data." },
  { id: "FIXTURE", name: "Fixture", variant: "fixture", mode: "--mode-fixture", bg: "--state-warn-bg", bd: "--state-warn-bd",
    desc: "Deterministic replay of the 2026-06-30 session." },
];

function EnvOption({ env, selected, onSelect }) {
  return (
    <button type="button" onClick={() => onSelect(env.id)}
      aria-pressed={selected}
      style={{
        display: "flex", alignItems: "center", gap: 12, width: "100%", textAlign: "left",
        padding: "10px 12px", cursor: "pointer", font: "inherit", boxSizing: "border-box",
        border: `1px solid var(${selected ? env.bd : "--border"})`,
        background: selected ? `var(${env.bg})` : "var(--bg-light)",
        boxShadow: selected ? `inset 2px 0 0 var(${env.mode})` : "none",
      }}>
      <span aria-hidden="true" style={{
        flex: "none", width: 12, height: 12, boxSizing: "border-box", borderRadius: "50%",
        border: `1px solid var(${selected ? env.mode : "--border-strong"})`,
        background: selected ? `radial-gradient(circle at center, var(${env.mode}) 0 3.5px, transparent 4px)` : "transparent",
      }}></span>
      <span style={{ flex: 1, minWidth: 0 }}>
        <span style={{ display: "block", fontSize: 12, fontWeight: 600, fontVariant: "all-small-caps",
          letterSpacing: ".05em", color: "var(--text-primary)" }}>{env.name}</span>
        <span style={{ display: "block", fontSize: 11.5, color: "var(--text-secondary)" }}>{env.desc}</span>
      </span>
      <Badge variant={env.variant} dot>{env.id}</Badge>
    </button>
  );
}

function SessionStartScreen({ environment = "PAPER", requireMfa = true }) {
  const [step, setStep] = useState(0);
  const [op, setOp] = useState("r.alvarez");
  const [pw, setPw] = useState("\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022");
  const [code, setCode] = useState("");
  const [env, setEnv] = useState(String(environment).toUpperCase());
  const [role, setRole] = useState("operator");
  const [confirm, setConfirm] = useState("");
  const [started, setStarted] = useState(false);

  const steps = requireMfa
    ? [{ label: "Credentials" }, { label: "Verify" }, { label: "Environment" }]
    : [{ label: "Credentials" }, { label: "Environment" }];
  const envStep = requireMfa ? 2 : 1;
  const live = env === "LIVE";
  const canStart = !live || confirm.trim().toUpperCase() === "LIVE";
  const mono = { fontFamily: "var(--font-data)" };

  const back = () => setStep((s) => Math.max(0, s - 1));

  return (
    <div style={{ height: "100vh", display: "flex", flexDirection: "column", background: "var(--bg)",
      fontFamily: "var(--font-body)", color: "var(--text-secondary)", fontSize: 13 }}>

      {/* Chrome band — no workstation topbar before a session exists */}
      <div style={{ flex: "none", height: 48, background: "var(--topbar-bg)", borderBottom: "1px solid var(--topbar-border)",
        display: "flex", alignItems: "center", gap: 12, padding: "0 16px" }}>
        <img src="../../assets/brand/meridian-mark-light.svg" alt="" style={{ height: 20, display: "block" }} />
        <span style={{ fontSize: 12, fontWeight: 600, letterSpacing: ".18em", color: "var(--topbar-text)" }}>MERIDIAN</span>
        <span style={{ ...mono, fontSize: 11, color: "var(--topbar-text-muted)" }}>workstation sign-in</span>
        <div style={{ flex: 1 }}></div>
        <span style={{ ...mono, fontSize: 11.5, color: "var(--topbar-text-muted)" }}>2026-07-05 14:32:08Z</span>
      </div>

      <main style={{ flex: 1, minHeight: 0, overflowY: "auto", display: "grid", placeItems: "center", padding: 32 }}>
        <div style={{ width: 448, display: "flex", flexDirection: "column", gap: 14 }}>
          <PanelSurface style={{ padding: 0 }}>
            <div style={{ padding: "13px 20px", borderBottom: "1px solid var(--border)",
              display: "flex", alignItems: "baseline", gap: 10 }}>
              <span style={{ font: "600 16px var(--font-display)", color: "var(--text-primary)" }}>Sign in</span>
              <div style={{ flex: 1 }}></div>
              <span style={{ ...mono, fontSize: 10.5, color: "var(--text-muted)" }}>auth.meridian.internal</span>
            </div>

            <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
              {!started && <Stepper steps={steps} activeStep={step} />}

              {started ? (
                <React.Fragment>
                  <StatusBanner tone="success" title="Session started"
                    detail={env + " \u00b7 role " + role + " \u00b7 2026-07-05 14:32:11Z \u00b7 session S-88412"} />
                  <Button variant="primary" onClick={() => { window.location.href = "../dashboard-workstation/DashboardWorkstation.dc.html"; }}>
                    Open workstation
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => { setStarted(false); setStep(0); setCode(""); setConfirm(""); }}>
                    Sign in as someone else
                  </Button>
                </React.Fragment>
              ) : step === 0 ? (
                <React.Fragment>
                  <Input label="Operator ID" value={op} onChange={(e) => setOp(e.target.value)} autoComplete="username" />
                  <Input label="Password" type="password" value={pw} onChange={(e) => setPw(e.target.value)} autoComplete="current-password" />
                  <Button variant="primary" disabled={!op.trim() || !pw} onClick={() => setStep(1)}>Continue</Button>
                  <span style={{ ...mono, fontSize: 10.5, color: "var(--text-muted)" }}>
                    Directory meridian.internal · SSO enforced for admin roles
                  </span>
                </React.Fragment>
              ) : requireMfa && step === 1 ? (
                <React.Fragment>
                  <Input label="Authentication code" value={code} inputMode="numeric" maxLength={6}
                    onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
                    style={{ letterSpacing: ".4em", fontSize: 18, textAlign: "center" }} />
                  <span style={{ fontSize: 11.5, color: "var(--text-secondary)" }}>
                    6-digit code from your authenticator · rotates every 30s
                  </span>
                  <div style={{ display: "flex", gap: 8 }}>
                    <Button variant="ghost" onClick={back}>Back</Button>
                    <div style={{ flex: 1 }}></div>
                    <Button variant="primary" disabled={!/^\d{6}$/.test(code)} onClick={() => setStep(envStep)}>Verify</Button>
                  </div>
                </React.Fragment>
              ) : (
                <React.Fragment>
                  <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                    {ENVS.map((e) => <EnvOption key={e.id} env={e} selected={env === e.id} onSelect={setEnv} />)}
                  </div>
                  <Select label="Role" value={role} onChange={setRole} options={[
                    { value: "operator", label: "Operator \u2014 trade, post, run" },
                    { value: "viewer", label: "Viewer \u2014 read-only" },
                    { value: "admin", label: "Admin \u2014 operator + configuration" },
                  ]} />
                  {live && (
                    <Input label={'Type "LIVE" to confirm real-money session'} value={confirm}
                      onChange={(e) => setConfirm(e.target.value)}
                      style={{ textTransform: "uppercase", letterSpacing: ".08em" }} />
                  )}
                  <div style={{ display: "flex", gap: 8 }}>
                    <Button variant="ghost" onClick={back}>Back</Button>
                    <div style={{ flex: 1 }}></div>
                    <Button variant="primary" disabled={!canStart} onClick={() => setStarted(true)}>Start session</Button>
                  </div>
                </React.Fragment>
              )}
            </div>
          </PanelSurface>

          <div style={{ display: "flex", justifyContent: "center", gap: 16, ...mono, fontSize: 10.5, color: "var(--text-muted)" }}>
            {[["Gateway", "12ms"], ["Market data", "live"], ["Auth directory", "ok"]].map(([l, v]) => (
              <span key={l} style={{ display: "inline-flex", alignItems: "center", gap: 5 }}>
                <span style={{ width: 6, height: 6, background: "var(--green)", display: "inline-block" }}></span>
                {l} · {v}
              </span>
            ))}
          </div>
          <div style={{ textAlign: "center", ...mono, fontSize: 10, color: "var(--text-disabled)" }}>
            Meridian 1.18.0 · us-east-1 · TLS 1.3 · Last sign-in 2026-07-04 21:18:42Z from 10.4.18.22
          </div>
        </div>
      </main>

      <StatusBar items={[
        { status: "ok", label: "Gateway", value: "us-east-1 \u00b7 12ms" },
        { status: "ok", label: "Auth", value: "directory ok" },
        { status: "ok", label: "Market data", value: "Polygon \u00b7 live" },
        { label: "Clock", value: "UTC", push: true },
        { label: "Build", value: "1.18.0" },
      ]} />
    </div>
  );
}

window.SessionStartScreen = SessionStartScreen;
if (typeof module !== "undefined") module.exports = { SessionStartScreen };
