// General Ledger view — toolbar (search + account filter + CSV export) over LedgerTable.
// Mounted by the DC via <x-import>. DS components resolve at render time (bundle is
// guaranteed present once the logic class flips `ready`).
const { useState, useMemo } = React;

function AcctLedgerView(props) {
  const { Toolbar, ToolbarGroup, ToolbarSpacer, Input, Select, Button, LedgerTable, EmptyState, Money } =
    window.MeridianDesignSystem_4f61be;
  const [q, setQ] = useState("");
  const [account, setAccount] = useState(props.initialAccount || "all");
  const rows = props.rows || [];

  const accounts = useMemo(
    () => Array.from(new Set(rows.map((r) => r.account))).sort(),
    [rows]
  );
  const filtered = useMemo(() => rows.filter((r) => {
    if (account !== "all" && r.account !== account) return false;
    const s = q.trim().toLowerCase();
    if (!s) return true;
    return [r.account, r.memo, r.ref].filter(Boolean).join(" ").toLowerCase().includes(s);
  }), [rows, q, account]);

  const live = filtered.filter((r) => r.status !== "void");
  const opening = account === "all" ? 0 : (props.openings || {})[account.slice(0, 4)] || 0;
  const caption =
    (account === "all" ? "All accounts" : account) +
    " · 2026-06 · " + live.length + " postings" +
    (filtered.length !== live.length ? " · " + (filtered.length - live.length) + " void" : "");

  const exportCsv = () => {
    const head = "date,ref,account,memo,debit,credit,status";
    const body = filtered.map((r) =>
      [r.date, r.ref, '"' + r.account + '"', '"' + (r.memo || "") + '"', r.debit || "", r.credit || "", r.status || "posted"].join(",")
    );
    const blob = new Blob([[head, ...body].join("\n")], { type: "text/csv" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "general-ledger-2026-06.csv";
    a.click();
    URL.revokeObjectURL(a.href);
  };

  return (
    <section data-screen-label="General ledger" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <Toolbar>
        <ToolbarGroup>
          <Input placeholder="Filter memo, ref, account…" value={q}
            onChange={(e) => setQ(e.target.value)} style={{ width: 230 }} aria-label="Filter postings" />
          <Select value={account} onChange={setAccount}
            options={[{ value: "all", label: "All accounts" }, ...accounts.map((a) => ({ value: a, label: a }))]} />
        </ToolbarGroup>
        <ToolbarSpacer />
        <ToolbarGroup>
          <Button variant="ghost" size="sm" onClick={exportCsv}>Export CSV</Button>
        </ToolbarGroup>
      </Toolbar>

      {filtered.length === 0 ? (
        <EmptyState icon="search" title="No postings match" detail={'Nothing in 2026-06 matches "' + q + '".'}
          action="Clear filters" onAction={() => { setQ(""); setAccount("all"); }} />
      ) : (
        <LedgerTable rows={filtered} currency="USD" showAccount opening={opening} caption={caption} />
      )}

      {props.lastRef && (
        <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
          Last posted {props.lastRef}
        </span>
      )}
    </section>
  );
}

window.AcctLedgerView = AcctLedgerView;
if (typeof module !== "undefined") module.exports = { AcctLedgerView };
