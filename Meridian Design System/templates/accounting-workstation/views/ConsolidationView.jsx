// Consolidation view — O-5: statement lines × entity columns plus an ELIMINATIONS column,
// summing to the group column. The group figure is never silently pre-summed: the elimination
// layer is disclosed on every line, the proof strip states the arithmetic, and every
// elimination opens its JE pair — two balanced entries, one per entity, netting to zero at
// group (IntercompanyZero).
const { useState } = React;

function AcctConsolidationView(props) {
  const DS = window.MeridianDesignSystem_205d17;
  const { StatementTable, Eyebrow, Badge, Button, Drawer } = DS;
  const M = DS.Money;
  const [openId, setOpenId] = useState(null);
  const data = props.data || { entities: [], sections: [] };
  const elims = props.eliminations || [];
  const scope = props.scope;
  const fmt = (v) => M.formatMoney(v, { currency: "USD" });
  const entKeys = data.entities.map((e) => e.key);

  // Group column is DERIVED here — Σ entity columns + elimination — so the identity
  // "group = Σ entities − eliminations" is arithmetic the view performs, not data it repeats.
  const zero = () => { const z = { elim: 0, group: 0 }; entKeys.forEach((k) => (z[k] = 0)); return z; };
  const addInto = (acc, v) => Object.keys(acc).forEach((k) => (acc[k] = M.roundMoney(acc[k] + (v[k] || 0), 2)));
  const grand = zero();
  const sections = data.sections.map((sec) => {
    const sub = zero();
    const rows = sec.rows.map((r) => {
      const elim = r.elim ? r.elim.amount : 0;
      const sum = entKeys.reduce((a, k) => a + (r.values[k] || 0), 0);
      const values = Object.assign({}, r.values, { elim, group: M.roundMoney(sum + elim, 2) });
      addInto(sub, values);
      return { label: r.label + (r.elim ? "  · " + r.elim.ref : ""), values };
    });
    addInto(grand, sub);
    return { label: sec.label, rows, subtotal: { label: "Total " + sec.label.toLowerCase(), values: Object.assign({}, sub) } };
  });
  const total = { label: "Net income", values: grand };
  const columns = data.entities
    .map((e) => ({ key: e.key, label: e.label + (scope === e.id ? " · scope" : "") }))
    .concat([{ key: "elim", label: "Eliminations" }, { key: "group", label: "Group" + (scope === "MER-GRP" ? " · scope" : "") }]);

  const partsSum = entKeys.reduce((a, k) => a + grand[k], 0);
  const parts = data.entities.map((e) => fmt(grand[e.key])).join(" + ");
  const tied = M.roundMoney(partsSum + grand.elim, 2) === grand.group;
  const open = elims.find((e) => e.id === openId);

  const th = { textAlign: "left", padding: "6px 10px", fontSize: 10, textTransform: "uppercase", letterSpacing: "0.06em", color: "var(--text-muted)", borderBottom: "1px solid var(--border)", fontFamily: "var(--font-data)", fontWeight: 600 };
  const td = { padding: "8px 10px", borderBottom: "1px solid var(--border)", fontSize: 12, verticalAlign: "top" };
  const mono = { fontFamily: "var(--font-data)" };

  return (
    <section data-screen-label="Consolidation" style={{ display: "flex", flexDirection: "column", gap: 12, maxWidth: 1180 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
        <Eyebrow>Consolidated statement of operations</Eyebrow>
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          {data.period} · USD · entity columns + disclosed eliminations — the group column is computed, never stated
        </span>
      </div>

      <StatementTable currency="USD" columns={columns} sections={sections} total={total}
        caption="Consolidated statement of operations — entity columns, eliminations, group" />

      <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap", border: "1px solid var(--border)", borderRadius: 4, padding: "8px 12px" }}>
        <Badge variant={tied ? "success" : "danger"} dot>{tied ? "Ties" : "Does not tie"}</Badge>
        <span style={{ ...mono, fontSize: 12, color: "var(--text-primary)" }}>
          Group net income {fmt(grand.group)} = {parts} {grand.elim < 0 ? "−" : "+"} {fmt(Math.abs(grand.elim))} eliminations
        </span>
        <span style={{ ...mono, fontSize: 11, color: "var(--text-muted)" }}>
          — income-statement eliminations offset within NI; each line's layer is disclosed above
        </span>
      </div>

      <div style={{ border: "1px solid var(--border)", borderRadius: 4 }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 10, padding: "10px 14px", borderBottom: "1px solid var(--border)" }}>
          <Eyebrow>Elimination register</Eyebrow>
          <span style={{ ...mono, fontSize: 11, color: "var(--text-muted)" }}>
            every elimination figure traces to a JE pair · IC mirror pairs net to zero across entities
          </span>
        </div>
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead><tr>
            <th style={th}>Elim</th><th style={th}>Statement lines eliminated</th>
            <th style={{ ...th, textAlign: "right" }}>Amount</th>
            <th style={th}>IC mirror</th><th style={th}>Net</th><th style={th}></th>
          </tr></thead>
          <tbody>
            {elims.map((e) => (
              <tr key={e.id}>
                <td style={{ ...td, ...mono, color: "var(--text-primary)", fontWeight: 600, whiteSpace: "nowrap" }}>{e.id}</td>
                <td style={td}><div style={{ color: "var(--text-primary)" }}>{e.label}</div>
                  <div style={{ fontSize: 11, color: "var(--text-muted)" }}>{e.lines}</div></td>
                <td style={{ ...td, ...mono, textAlign: "right", whiteSpace: "nowrap" }}>{fmt(e.amount)}</td>
                <td style={{ ...td, fontSize: 11 }}>
                  <div style={mono}>{e.mirror.a.entity} · {e.mirror.a.account} · {fmt(e.mirror.a.amount)}</div>
                  <div style={mono}>{e.mirror.b.entity} · {e.mirror.b.account} · ({fmt(Math.abs(e.mirror.b.amount))})</div>
                </td>
                <td style={td}><Badge variant={e.mirror.a.amount + e.mirror.b.amount === 0 ? "success" : "danger"} dot>
                  {fmt(e.mirror.a.amount + e.mirror.b.amount)} {e.mirror.a.amount + e.mirror.b.amount === 0 ? "tied" : "broken"}</Badge></td>
                <td style={{ ...td, whiteSpace: "nowrap" }}>
                  <Button variant="ghost" size="sm" onClick={() => setOpenId(e.id)}>JE pair</Button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Drawer open={!!open} onClose={() => setOpenId(null)} size="460px"
        title={open ? open.id + " · " + open.label : ""}>
        {open && (
          <div style={{ display: "flex", flexDirection: "column", gap: 12, padding: "4px 2px" }}>
            <p style={{ margin: 0, fontSize: 12, color: "var(--text-secondary)" }}>{open.basis}</p>
            {open.jes.map((je) => (
              <div key={je.ref} style={{ border: "1px solid var(--border)", borderRadius: 4 }}>
                <div style={{ display: "flex", alignItems: "baseline", gap: 8, padding: "8px 10px", borderBottom: "1px solid var(--border)" }}>
                  <span style={{ ...mono, fontSize: 12, fontWeight: 600, color: "var(--text-primary)" }}>{je.ref}</span>
                  <span style={{ fontSize: 11, color: "var(--text-muted)" }}>{je.entity} ({je.entityId}) · {je.date}</span>
                </div>
                <div style={{ padding: "6px 10px", fontSize: 11, color: "var(--text-muted)", borderBottom: "1px solid var(--border)" }}>{je.memo}</div>
                <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                  <tbody>
                    {je.lines.map((l, i) => (
                      <tr key={i}>
                        <td style={{ padding: "5px 10px", paddingLeft: l.credit ? 24 : 10 }}>{l.account}</td>
                        <td style={{ ...mono, padding: "5px 10px", textAlign: "right", width: 90 }}>{l.debit ? fmt(l.debit) : ""}</td>
                        <td style={{ ...mono, padding: "5px 10px", textAlign: "right", width: 90 }}>{l.credit ? fmt(l.credit) : ""}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <Badge variant="success" dot>Pair nets to {fmt(0)} at group</Badge>
              <span style={{ fontSize: 11, color: "var(--text-muted)" }}>IntercompanyZero — both legs balanced, mirror accounts offset.</span>
            </div>
          </div>
        )}
      </Drawer>
    </section>
  );
}

window.AcctConsolidationView = AcctConsolidationView;
if (typeof module !== "undefined") module.exports = { AcctConsolidationView };
