// Chart of Accounts view — AccountTree beside an account inspector (facts derived live from
// the ledger), with post-to-account and view-postings cross actions.
const { useState, useMemo } = React;

function rollup(node) {
  if (node.children && node.children.length) {
    return node.children.reduce((s, c) => s + rollup(c), 0);
  }
  return Number(node.balance) || 0;
}
function findNode(nodes, code) {
  for (const n of nodes) {
    if (n.code === code) return n;
    if (n.children) { const hit = findNode(n.children, code); if (hit) return hit; }
  }
  return null;
}

function AcctAccountsView(props) {
  const { AccountTree, PanelSurface, Eyebrow, KeyValueGrid, Button, Money } =
    window.MeridianDesignSystem_4f61be;
  const [sel, setSel] = useState("1100");
  const coa = props.coa || [];
  const ledger = props.ledger || [];

  const node = useMemo(() => findNode(coa, sel) || { code: sel, name: "—", type: "asset" }, [coa, sel]);
  const postings = useMemo(
    () => ledger.filter((r) => r.account.slice(0, 4) === node.code && r.status !== "void"),
    [ledger, node]
  );
  const lastDate = postings.reduce((m, r) => (r.date > m ? r.date : m), "");
  const isParent = !!(node.children && node.children.length);
  const balance = rollup(node);

  return (
    <section data-screen-label="Chart of accounts"
      style={{ display: "grid", gridTemplateColumns: "minmax(0,1fr) 320px", gap: 14, alignItems: "start" }}>
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 10, flexWrap: "wrap" }}>
          <Eyebrow>Chart of accounts</Eyebrow>
          <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
            As of 2026-06-30 · parents roll up children · click a row to inspect
          </span>
        </div>
        <AccountTree nodes={coa} currency="USD" defaultExpandedDepth={1}
          selectedCode={sel} onSelect={(n) => setSel(n.code)} valueLabel="Balance" />
      </div>

      <PanelSurface raised style={{ padding: 14, display: "flex", flexDirection: "column", gap: 12 }}>
        <Eyebrow>Account inspector</Eyebrow>
        <div style={{ font: "600 15px var(--font-display)", color: "var(--text-primary)" }}>
          {node.code} · {node.name}
        </div>
        <KeyValueGrid columns={2} items={[
          { label: "Type", value: node.type || "—" },
          { label: "Normal side", value: Money.accountNormalSide(node.type) },
          { label: "Balance", value: Money.formatMoney(balance, { currency: "USD", parens: true }) },
          { label: "Postings · June", value: postings.length ? postings.length + " lines" : "—" },
          { label: "Last activity", value: lastDate || "—" },
          { label: "Level", value: isParent ? "roll-up" : "posting" },
        ]} />
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <Button variant="ghost" size="sm" disabled={props.locked || isParent} style={{ whiteSpace: "nowrap" }}
            onClick={() => props.onPostTo && props.onPostTo(node.code + " · " + node.name)}>
            Post entry
          </Button>
          <Button variant="ghost" size="sm" disabled={!postings.length} style={{ whiteSpace: "nowrap" }}
            onClick={() => props.onViewLedger && props.onViewLedger(node.code)}>
            View postings
          </Button>
        </div>
      </PanelSurface>
    </section>
  );
}

window.AcctAccountsView = AcctAccountsView;
if (typeof module !== "undefined") module.exports = { AcctAccountsView };
