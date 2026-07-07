// Period close view — the close pipeline as a gate rail, open items, and the lock panel.
// Lock is enabled only when reconciliation is clear and the FX reval is posted; locking
// makes the whole workstation read-only (Reopen reverses it).
function AcctPeriodCloseView(props) {
  const { GateRail, ValidationIssueList, ReadinessPanel, PanelSurface, Eyebrow, Button, KeyValueGrid } =
    window.MeridianDesignSystem_4f61be;
  const gates = props.gates || [];
  const passed = gates.filter((g) => g.status === "Passed").length;

  return (
    <section data-screen-label="Period close" style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Period close · 2026-Q2</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          Every gate must pass before the period can lock
        </span>
      </div>

      <GateRail gates={gates} />

      <div style={{ display: "flex", flexWrap: "wrap", gap: 14, alignItems: "flex-start" }}>
        <PanelSurface flat style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10, flex: "1 1 300px", minWidth: 0 }}>
          <Eyebrow>Open items</Eyebrow>
          <ValidationIssueList dense issues={props.issues || []}
            emptyLabel="No open items — period is ready to lock." />
          {(props.recOpen > 0 || !props.fxPosted) && !props.locked && (
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              {props.recOpen > 0 && (
                <Button variant="link" size="sm" onClick={() => props.onGoto && props.onGoto("recon")}>
                  Go to reconciliation
                </Button>
              )}
              {!props.fxPosted && (
                <Button variant="link" size="sm" onClick={() => props.onGoto && props.onGoto("fx")}>
                  Go to FX revaluation
                </Button>
              )}
            </div>
          )}
        </PanelSurface>

        <div style={{ flex: "0 1 380px", minWidth: 300 }}>
          <ReadinessPanel
          state={props.locked ? "Passed" : props.canLock ? "Ready" : "ReviewRequired"}
          statusLabel={props.locked ? "Locked" : undefined}
          score={passed + " / " + gates.length + " gates"}
          title="Fiscal period 2026-Q2"
          detail={props.locked
            ? "Books are read-only. Reopening requires controller authorization."
            : props.canLock
              ? "All close gates passed. Locking prevents further postings in the period."
              : "Resolve the open items before locking — the gate rail shows what remains."}
          actions={props.locked ? (
            <Button variant="ghost" size="sm" onClick={() => props.onReopen && props.onReopen()}>
              Reopen period
            </Button>
          ) : (
            <Button variant="primary" size="sm" disabled={!props.canLock}
              onClick={() => props.onLock && props.onLock()}>
              Lock period
            </Button>
          )}
        >
          <KeyValueGrid columns={2} items={[
            { label: "Basis", value: "USD · accrual" },
            { label: "Postings", value: (props.postings || 0) + " lines" },
            { label: "Opened", value: "2026-04-01" },
            { label: "Locked", value: props.lockedAt || "—" },
          ]} />
          </ReadinessPanel>
        </div>
      </div>
    </section>
  );
}

window.AcctPeriodCloseView = AcctPeriodCloseView;
if (typeof module !== "undefined") module.exports = { AcctPeriodCloseView };
