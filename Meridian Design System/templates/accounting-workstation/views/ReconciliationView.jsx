// Reconciliation view — statement vs ledger for the cash account. The one open break is
// resolved by posting the prefilled accrual entry (onPostAccrual → journal drawer).
function AcctReconciliationView(props) {
  const { ReconciliationPanel, Eyebrow, Callout, Button, MetricCard, Money } =
    window.MeridianDesignSystem_4f61be;
  const left = props.left || [];
  const right = props.right || [];
  const open = left.filter((i) => !i.matched);
  const leftSum = Money.sumAmounts(left.map((i) => i.amount));
  const rightSum = Money.sumAmounts(right.map((i) => i.amount));
  const variance = Money.roundMoney(leftSum - rightSum, 2);
  const fmt = (v) => Money.formatMoney(v, { currency: "USD", parens: true });

  return (
    <section data-screen-label="Reconciliation" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Cash reconciliation</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          Account 1100 · 2026-06-01 → 06-30 · {left.length - open.length} matched · {open.length} open
        </span>
      </div>

      {open.length > 0 ? (
        <Callout tone="warning" title={"Unmatched statement line · " + fmt(variance) + " variance"}>
          <div style={{ display: "flex", flexDirection: "column", gap: 8, alignItems: "flex-start" }}>
            <span>
              {open[0].ref} · {open[0].memo} · {fmt(open[0].amount)} has no ledger counterpart.
              Post the accrual to clear the break.
            </span>
            <Button variant="primary" size="sm" disabled={props.locked}
              onClick={() => props.onPostAccrual && props.onPostAccrual()}>
              Post accrual entry
            </Button>
          </div>
        </Callout>
      ) : (
        <Callout tone="success" title="Reconciled">
          Statement and ledger agree for the period — variance {fmt(0)}.
        </Callout>
      )}

      <ReconciliationPanel left={{ title: "Statement", items: left }}
        right={{ title: "Ledger", items: right }} currency="USD" tolerance={0.01} />

      <div style={{ display: "grid", gridTemplateColumns: "repeat(3, minmax(0,1fr))", gap: 12 }}>
        <MetricCard label="Statement total" value={fmt(leftSum)} delta={left.length + " lines"} tone="neutral" />
        <MetricCard label="Ledger total" value={fmt(rightSum)} delta={right.length + " lines"} tone="neutral" />
        <MetricCard label="Variance" value={fmt(variance)}
          delta={variance === 0 ? "within tolerance" : "above 0.01 tolerance"}
          tone={variance === 0 ? "success" : "danger"} />
      </div>
    </section>
  );
}

window.AcctReconciliationView = AcctReconciliationView;
if (typeof module !== "undefined") module.exports = { AcctReconciliationView };
