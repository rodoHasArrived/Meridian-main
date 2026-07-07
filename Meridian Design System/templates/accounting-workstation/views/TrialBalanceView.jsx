// Trial Balance view — the period-end proof. Rows arrive normal-side signed from data.js;
// TrialBalance groups by type, subtotals, and proves Σdebit = Σcredit in its footer.
function AcctTrialBalanceView(props) {
  const { TrialBalance, Eyebrow } = window.MeridianDesignSystem_4f61be;
  return (
    <section data-screen-label="Trial balance" style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 940 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Trial balance</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          As of {props.asOf || "2026-06-30"} · pre-closing · footer proves Σ debit = Σ credit
        </span>
      </div>
      <TrialBalance rows={props.rows || []} currency="USD" />
    </section>
  );
}

window.AcctTrialBalanceView = AcctTrialBalanceView;
if (typeof module !== "undefined") module.exports = { AcctTrialBalanceView };
