// Trial Balance view — the period-end proof. Rows arrive normal-side signed from data.js;
// TrialBalance groups by type, subtotals, and proves Σdebit = Σcredit in its footer.
// 4.3 · O-4: an assurance column — who vouched for each balance, and whether the signature still
// binds the number on the row. Click a row → the Account Sheet.
function AcctTrialBalanceView(props) {
  const DS = window.MeridianDesignSystem_205d17;
  const { TrialBalance, Eyebrow, Assurance } = DS;
  const A = Assurance, records = props.assurance || {}, balances = props.balances || {};
  const trailing = A ? (row) => {
    const code = String(row.code || "").replace(/^\[[^\]]+\]\s*/, "").slice(0, 4);
    if (!/^\d{4}$/.test(code) || balances[code] === undefined) return null;
    return <A.Chip record={records[code]} balance={balances[code]} showActor />;
  } : undefined;
  return (
    <section data-screen-label="Trial balance" style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: A ? 1180 : 940 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Trial balance</Eyebrow>
        {props.scopeLine && <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-primary)" }}>{props.scopeLine}</span>}
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          As of {props.asOf || "2026-06-30"} · pre-closing · footer proves Σ debit = Σ credit
        </span>
        {props.certLine && <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-secondary)", marginLeft: "auto" }}>{props.certLine}</span>}
      </div>
      <TrialBalance rows={props.rows || []} currency="USD" trailing={trailing}
        onRowClick={props.onOpenAccount ? (row) => { const c = String(row.code || "").replace(/^\[[^\]]+\]\s*/, "").slice(0, 4); if (/^\d{4}$/.test(c)) props.onOpenAccount(c); } : undefined} />
      {A && <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
        A signature binds to the balance it was given. A struck-through stage means the balance has moved since — the signature is void, not stale, and the chain restarts from the Account Sheet.
      </span>}
    </section>
  );
}

window.AcctTrialBalanceView = AcctTrialBalanceView;
if (typeof module !== "undefined") module.exports = { AcctTrialBalanceView };
