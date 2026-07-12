// Meridian settings-admin — template screen. Mounted by the DC via <x-import>; reads
// design-system components from the compiled bundle. The forms-heavy surface the other
// templates don't demonstrate: FormField stack, validation issues, TagInput, and a
// danger zone with a Dialog confirm gate.

const {
  WorkstationTopbar, StatusBar, PanelSurface, Eyebrow, Button, Badge,
  FormField, Input, Select, Checkbox, Toggle, RadioGroup, NumberInput, TagInput,
  ValidationIssueList, Dialog, DialogBody, DialogFooter, ToastProvider, Callout,
} = window.MeridianDesignSystem_4f61be;
const { useState } = React;

const SECTIONS = ["Organization", "Data providers", "Notifications", "Danger zone"];

function Section({ id, title, children, tone }) {
  return (
    <PanelSurface id={id} style={{ padding: 18, display: "flex", flexDirection: "column", gap: 14, borderColor: tone === "danger" ? "var(--red)" : undefined }}>
      <h2 style={{ font: "600 15px var(--font-display)", margin: 0, color: tone === "danger" ? "var(--red-dim)" : "var(--text-primary)" }}>{title}</h2>
      {children}
    </PanelSurface>
  );
}

function SettingsAdminScreen() {
  const [orgName, setOrgName] = useState("Meridian Capital Research");
  const [tz] = useState("UTC");
  const [recipients, setRecipients] = useState(["ops@meridian.example", "r.alvarez@meridian.example"]);
  const [rateLimit, setRateLimit] = useState(120);
  const [autoBackfill, setAutoBackfill] = useState(true);
  const [alertPolicy, setAlertPolicy] = useState("critical");
  const [dirty, setDirty] = useState(false);
  const [wipeOpen, setWipeOpen] = useState(false);
  const [wipeText, setWipeText] = useState("");
  const touch = (setter) => (v) => { setter(v); setDirty(true); };

  const issues = [];
  if (!orgName.trim()) issues.push({ code: "ORG-001", severity: "Critical", message: "Organization name is required.", gate: "Organization" });
  if (recipients.length === 0) issues.push({ code: "NOTIF-011", severity: "Warning", message: "No notification recipients — critical alerts will only reach the status bar.", gate: "Notifications" });

  return (
    <React.Fragment>
      <ToastProvider />
      <WorkstationTopbar moduleLabel="Settings" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        <nav aria-label="Settings sections" style={{ width: 200, flex: "none", background: "var(--sidebar-bg)", borderRight: "1px solid var(--sidebar-border)", padding: "14px 0", display: "flex", flexDirection: "column", gap: 2 }}>
          {SECTIONS.map((s, i) => (
            <a key={s} href={"#sect-" + i} style={{
              padding: "7px 16px", fontSize: 13, textDecoration: "none",
              color: s === "Danger zone" ? "var(--red-dim)" : "var(--nav-item)",
              borderLeft: i === 0 ? "2px solid var(--sidebar-active-ind)" : "2px solid transparent",
              background: i === 0 ? "var(--sidebar-active)" : "transparent",
            }}>{s}</a>
          ))}
        </nav>
        <main style={{ flex: 1, minWidth: 0, overflowY: "auto", padding: 18 }}>
          <div style={{ maxWidth: 720, margin: "0 auto", display: "flex", flexDirection: "column", gap: 14 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Settings</h1>
              {dirty && <Badge variant="warning">Unsaved changes</Badge>}
              <div style={{ flex: 1 }}></div>
              <Button variant="ghost" size="sm" disabled={!dirty} onClick={() => { setDirty(false); }}>Discard</Button>
              <Button variant="primary" size="sm" disabled={!dirty || issues.some((i) => i.severity === "Critical")}
                onClick={() => { setDirty(false); window.MeridianToast.success("Settings saved", "Applied to all workstations · 14:32:08Z"); }}>
                Save changes
              </Button>
            </div>

            {issues.length > 0 && <ValidationIssueList issues={issues} dense />}

            <Section id="sect-0" title="Organization">
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14 }}>
                <FormField label="Organization name" required error={!orgName.trim() ? "Required" : undefined}>
                  <Input value={orgName} onChange={(e) => touch(setOrgName)(e.target.value)} />
                </FormField>
                <FormField label="Display timezone" hint="All Meridian surfaces render UTC; this affects exports only.">
                  <Select value={tz} options={["UTC", "America/New_York", "Europe/London"]} onChange={() => setDirty(true)} />
                </FormField>
              </div>
            </Section>

            <Section id="sect-1" title="Data providers">
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, alignItems: "start" }}>
                <FormField label="API rate limit" hint="Requests per minute across all provider connections.">
                  <NumberInput value={rateLimit} min={10} max={600} step={10} onChange={touch(setRateLimit)} />
                </FormField>
                <FormField label="Collection behavior">
                  <div style={{ display: "flex", flexDirection: "column", gap: 8, paddingTop: 4 }}>
                    <Checkbox checked={autoBackfill} onChange={touch(setAutoBackfill)} label="Auto-backfill detected gaps" hint="Runs nightly against the primary provider." />
                    <Toggle checked={true} onChange={() => setDirty(true)} label="Validate bars on ingest" />
                  </div>
                </FormField>
              </div>
            </Section>

            <Section id="sect-2" title="Notifications">
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, alignItems: "start" }}>
                <FormField label="Recipients" hint="Enter to add. Critical alerts page the first recipient." error={recipients.length === 0 ? "At least one recipient recommended" : undefined}>
                  <TagInput value={recipients} onChange={touch(setRecipients)} placeholder="Add email…" aria-label="Notification recipients" />
                </FormField>
                <FormField label="Email policy">
                  <RadioGroup value={alertPolicy} onChange={touch(setAlertPolicy)} options={[
                    { value: "all", label: "All alerts", hint: "Every severity, every rule." },
                    { value: "critical", label: "Critical only", hint: "Warnings stay in the workstation." },
                    { value: "none", label: "None", hint: "In-app surfaces only." },
                  ]} />
                </FormField>
              </div>
            </Section>

            <Section id="sect-3" title="Danger zone" tone="danger">
              <Callout tone="danger" title="Destructive actions">These operate on the shared archive. They cannot be undone from the workstation.</Callout>
              <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
                <Button variant="danger" size="sm" onClick={() => setWipeOpen(true)}>Purge fixture data…</Button>
                <span style={{ fontSize: 12, color: "var(--text-muted)" }}>Removes all fixture-environment bars, orders, and runs.</span>
              </div>
            </Section>
          </div>
        </main>
      </div>
      <StatusBar items={[
        { status: "ok", label: "Config", value: dirty ? "draft" : "synced" },
        { label: "Version", value: "cfg-2214" },
        { status: "ok", label: "Providers", value: "3 connected", push: true },
      ]} />

      <Dialog open={wipeOpen} onClose={() => { setWipeOpen(false); setWipeText(""); }} title="Purge fixture data">
        <DialogBody>
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <p style={{ margin: 0, fontSize: 13, color: "var(--text-secondary)", lineHeight: 1.5 }}>
              This deletes <strong>all fixture-environment data</strong> — 1.2M bars, 4,882 orders, 312 runs. Live and paper data are not affected.
            </p>
            <FormField label='Type "purge fixtures" to confirm'>
              <Input value={wipeText} onChange={(e) => setWipeText(e.target.value)} placeholder="purge fixtures" />
            </FormField>
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={() => { setWipeOpen(false); setWipeText(""); }}>Cancel</Button>
          <Button variant="danger" disabled={wipeText !== "purge fixtures"}
            onClick={() => { setWipeOpen(false); setWipeText(""); window.MeridianToast.warning("Purge queued", "Fixture data purge running — progress in the status bar."); }}>
            Purge fixture data
          </Button>
        </DialogFooter>
      </Dialog>
    </React.Fragment>
  );
}

window.SettingsAdminScreen = SettingsAdminScreen;
if (typeof module !== "undefined") module.exports = { SettingsAdminScreen };
