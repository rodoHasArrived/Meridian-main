// Tax lots view — cost basis and holding-period classification, unrealized ⇄ realized basis.
const { useState } = React;

function AcctTaxLotsView(props) {
  const { TaxLotTable, Eyebrow, SegmentedControl } = window.MeridianDesignSystem_4f61be;
  const [mode, setMode] = useState("unrealized");
  const asOf = props.asOf || "2026-06-30";

  return (
    <section data-screen-label="Tax lots" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Tax lots · cost basis</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          As of {asOf} · long-term ≥ 365 days
        </span>
        <div style={{ flex: 1 }}></div>
        <SegmentedControl size="sm" value={mode} onChange={setMode}
          options={[{ value: "unrealized", label: "Unrealized" }, { value: "realized", label: "Realized" }]} />
      </div>
      <TaxLotTable lots={props.lots || []} currency="USD" asOf={asOf} mode={mode} showSymbol
        caption={"Tax lots · " + mode + " basis · as of " + asOf} />
    </section>
  );
}

window.AcctTaxLotsView = AcctTaxLotsView;
if (typeof module !== "undefined") module.exports = { AcctTaxLotsView };
