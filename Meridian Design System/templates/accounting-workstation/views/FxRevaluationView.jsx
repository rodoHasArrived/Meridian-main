// FX revaluation view — foreign balances at booked vs current rates. The net unrealized G/L
// becomes a prefilled revaluation journal entry (onPostReval → journal drawer).
function AcctFxRevalView(props) {
  const { FxRevaluationTable, Eyebrow, Callout, Button, Money } = window.MeridianDesignSystem_4f61be;
  const net = props.netGl || 0;
  const fmt = (v) => Money.formatMoney(v, { currency: "USD", signed: true });

  return (
    <section data-screen-label="FX revaluation" style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 980 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>FX revaluation</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          Base USD · booked vs 2026-06-30 rates · rates are USD per 1 local unit
        </span>
      </div>

      <FxRevaluationTable base="USD" rows={props.rows || []} />

      {props.fxPosted ? (
        <Callout tone="success" title={"Revaluation posted · " + (props.fxRef || "")}>
          Foreign balances are carried at 2026-06-30 rates. Unrealized G/L of {fmt(net)} is on the books.
        </Callout>
      ) : (
        <Callout tone="info" title={"Net unrealized FX G/L · " + fmt(net)}>
          <div style={{ display: "flex", flexDirection: "column", gap: 8, alignItems: "flex-start" }}>
            <span>
              Posting the revaluation carries foreign balances at current rates:
              {net >= 0
                ? " Dr 1400 · FX translation adjustment / Cr 4300 · Unrealized FX gain/loss."
                : " Dr 4300 · Unrealized FX gain/loss / Cr 1400 · FX translation adjustment."}
            </span>
            <Button variant="primary" size="sm" disabled={props.locked}
              onClick={() => props.onPostReval && props.onPostReval()}>
              Post revaluation entry
            </Button>
          </div>
        </Callout>
      )}
    </section>
  );
}

window.AcctFxRevalView = AcctFxRevalView;
if (typeof module !== "undefined") module.exports = { AcctFxRevalView };
