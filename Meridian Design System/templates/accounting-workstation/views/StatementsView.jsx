// Statements view — statement of operations with an as-of date and a Q2-only vs
// Q2-vs-Q1 comparison switch.
const { useState } = React;

function AcctStatementsView(props) {
  const { StatementTable, Eyebrow, Toolbar, ToolbarGroup, ToolbarSpacer, SegmentedControl, DatePicker, Button } =
    window.MeridianDesignSystem_4f61be;
  const [asOf, setAsOf] = useState("2026-06-30");
  const [mode, setMode] = useState("compare");
  const q2 = (props.total && props.total.values && props.total.values.q2) || 0;
  const q1 = (props.total && props.total.values && props.total.values.q1) || 1;
  const qoq = ((q2 - q1) / q1) * 100;

  const columns = mode === "compare"
    ? [{ key: "q2", label: "Q2 2026" }, { key: "q1", label: "Q1 2026" }]
    : [{ key: "q2", label: "Q2 2026" }];

  return (
    <section data-screen-label="Statements" style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 940 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Statement of operations</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          USD · negatives in parentheses · net income {(qoq >= 0 ? "+" : "") + qoq.toFixed(1)}% vs Q1
        </span>
      </div>

      <Toolbar>
        <ToolbarGroup>
          <div style={{ minWidth: 240, paddingRight: 26 }}>
            <DatePicker label="As of" value={asOf} onChange={setAsOf} />
          </div>
          <SegmentedControl size="sm" value={mode} onChange={setMode}
            options={[{ value: "q2", label: "Q2 only" }, { value: "compare", label: "Q2 vs Q1" }]} />
        </ToolbarGroup>
        <ToolbarSpacer />
        <ToolbarGroup>
          <Button variant="ghost" size="sm" onClick={() => window.print()}>Print / PDF</Button>
        </ToolbarGroup>
      </Toolbar>

      <StatementTable currency="USD" columns={columns}
        sections={props.sections || []} total={props.total} />
    </section>
  );
}

window.AcctStatementsView = AcctStatementsView;
if (typeof module !== "undefined") module.exports = { AcctStatementsView };
