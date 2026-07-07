// Meridian journaling workstation — comprehensive double-entry posting interface
// Integrates balanced form, posting history, search/filter, and reconciliation tie-in
const {
  WorkstationTopbar, NavRail, StatusBar, Eyebrow, Button, Input, Select, Badge, Modal,
  ModalHeader, ModalBody, ModalFooter, Tabs, TabPanel, DenseDataTable, JournalEntryForm,
  ReconciliationPanel, FilteredDataTable, EmptyState, Pagination, Toast, ToastProvider
} = window.MeridianDesignSystem_4f61be;
const { useState, useMemo } = React;

/* ── CSS ── */
(function injectCss() {
  if (document.getElementById("journ-css")) return;
  const el = document.createElement("style");
  el.id = "journ-css";
  el.textContent = `
.journ-layout { display: flex; flex-direction: column; height: 100vh; background: var(--bg); color: var(--text-primary); font-family: var(--font-body); }
.journ-main { display: flex; flex: 1; min-height: 0; }
.journ-content { flex: 1; min-width: 0; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 16px; }
.journ-section { display: flex; flex-direction: column; gap: 12px; }
.journ-section-head { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.journ-search { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.journ-search input { padding: 8px 12px; border: 1px solid var(--border); background: var(--bg-light); font-size: 13px; width: 220px; }
.journ-search input:focus { outline: none; border-color: var(--border-focus); box-shadow: 0 0 0 2px rgba(47,111,143,.2); }
.journ-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.journ-table th { background: var(--bg-medium); padding: 9px 12px; text-align: left; border: 1px solid var(--border); font-weight: 600; font-size: 11px; }
.journ-table td { padding: 10px 12px; border: 1px solid var(--border); height: 40px; }
.journ-table td.mono { font-family: var(--font-data); color: var(--text-muted); }
.journ-table td.num { text-align: right; font-family: var(--font-data); }
.journ-entry-row { cursor: pointer; transition: background 120ms; }
.journ-entry-row:hover { background: var(--bg-hover); }
.journ-entry-row.unreconciled { background: var(--amber-a10,rgba(197,136,26,.06)); }
.journ-entry-row.reconciled { background: var(--green-a10,rgba(22,136,95,.06)); }
.journ-modal-hd { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 15px 18px; border-bottom: 1px solid var(--border); background: var(--bg-light); }
.journ-modal-body { padding: 18px; overflow-y: auto; max-height: 70vh; }
.journ-modal-title { font-size: 16px; font-weight: 600; color: var(--text-primary); }
.journ-close-btn { appearance: none; border: 1px solid var(--border); background: var(--bg-light); width: 28px; height: 28px; border-radius: 4px; cursor: pointer; display: flex; align-items: center; justify-content: center; }
.journ-close-btn:hover { background: var(--bg-hover); }
.journ-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px; }
.journ-stat-card { padding: 12px; background: var(--bg-light); border: 1px solid var(--border); border-radius: 4px; }
.journ-stat-label { font-size: 11px; color: var(--text-muted); }
.journ-stat-value { font-size: 16px; font-weight: 600; font-family: var(--font-data); margin-top: 4px; }
`;
  document.head.appendChild(el);
})();

/* ── Seed data ── */
const ACCOUNTS = [
  "1100 · Cash", "1200 · Brokerage receivable", "1500 · Custody account",
  "4100 · Realized trading gains", "4200 · Dividend income",
  "5100 · Commissions & fees", "5300 · Financing cost",
];

const SEED_ENTRIES = [
  { id: 1, date: "06-02", ref: "JE-1042", memo: "Client settlement — AAPL block", lines: [
    { account: "1100 · Cash", debit: 82440, credit: 0 },
    { account: "1200 · Brokerage rec.", debit: 0, credit: 82440 },
  ], posted: true, reconciled: true },
  { id: 2, date: "06-03", ref: "JE-1043", memo: "Commission & exchange fees", lines: [
    { account: "5100 · Commissions & fees", debit: 318.5, credit: 0 },
    { account: "1100 · Cash", debit: 0, credit: 318.5 },
  ], posted: true, reconciled: false },
  { id: 3, date: "06-09", ref: "JE-1060", memo: "Dividend receivable — MSFT", lines: [
    { account: "1200 · Brokerage rec.", debit: 1290, credit: 0 },
    { account: "4200 · Dividend income", debit: 0, credit: 1290 },
  ], posted: true, reconciled: true },
];

/* ── Components ── */
function JournalEntryModal({ open, onClose, accounts, onPost, entry }) {
  return (
    <Modal open={open} onClose={onClose}>
      <div className="journ-modal-hd">
        <div>
          <Eyebrow>{entry ? "Edit journal entry" : "New journal entry"}</Eyebrow>
          <div className="journ-modal-title">Record a balanced posting</div>
        </div>
        <button className="journ-close-btn" onClick={onClose}>×</button>
      </div>
      <div className="journ-modal-body">
        <JournalEntryForm
          initialHeader={entry ? { date: entry.date, ref: entry.ref, memo: entry.memo } : { date: new Date().toISOString().slice(0,10), ref: "JE-" }}
          initialLines={entry ? entry.lines : [{ account: "", debit: "", credit: "" }, { account: "", debit: "", credit: "" }]}
          accounts={accounts}
          currency="USD"
          onPost={(data) => {
            onPost(data);
            onClose();
          }}
        />
      </div>
    </Modal>
  );
}

function PostingHistory({ entries, onEdit, onReconcile }) {
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("all");

  const filtered = useMemo(() => {
    let result = entries;

    if (filter === "posted") result = result.filter(e => e.posted);
    if (filter === "draft") result = result.filter(e => !e.posted);
    if (filter === "reconciled") result = result.filter(e => e.reconciled);
    if (filter === "unreconciled") result = result.filter(e => !e.reconciled);

    const q = search.toLowerCase();
    if (q) {
      result = result.filter(e =>
        e.ref.toLowerCase().includes(q) ||
        e.memo.toLowerCase().includes(q) ||
        e.lines.some(l => l.account.toLowerCase().includes(q))
      );
    }

    return result;
  }, [entries, search, filter]);

  return (
    <div className="journ-section">
      <div className="journ-section-head">
        <div>
          <Eyebrow>Posting history</Eyebrow>
          <span style={{ fontSize: 11, color: "var(--text-muted)" }}>{filtered.length} of {entries.length} entries</span>
        </div>
        <div className="journ-search">
          <input type="text" placeholder="Search by ref, memo, account…" value={search} onChange={(e) => setSearch(e.target.value)} />
          <select value={filter} onChange={(e) => setFilter(e.target.value)} style={{ padding: "8px 10px", border: "1px solid var(--border)", background: "var(--bg-light)", fontSize: 12, cursor: "pointer" }}>
            <option value="all">All entries</option>
            <option value="posted">Posted only</option>
            <option value="reconciled">Reconciled</option>
            <option value="unreconciled">Unreconciled</option>
          </select>
        </div>
      </div>

      {filtered.length === 0 ? (
        <EmptyState icon="inbox" title="No entries found" detail="Try a different search or filter" />
      ) : (
        <table className="journ-table">
          <thead>
            <tr>
              <th>Date</th>
              <th>Reference</th>
              <th>Memo</th>
              <th className="num">Debits</th>
              <th className="num">Credits</th>
              <th>Status</th>
              <th style={{ width: 100 }}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((e) => {
              const debits = e.lines.reduce((sum, l) => sum + (l.debit || 0), 0);
              const credits = e.lines.reduce((sum, l) => sum + (l.credit || 0), 0);
              return (
                <tr key={e.id} className={`journ-entry-row ${e.reconciled ? "reconciled" : "unreconciled"}`}>
                  <td className="mono">{e.date}</td>
                  <td className="mono">{e.ref}</td>
                  <td>{e.memo}</td>
                  <td className="num">${debits.toFixed(2)}</td>
                  <td className="num">${credits.toFixed(2)}</td>
                  <td>
                    <Badge variant={e.reconciled ? "success" : "warning"}>
                      {e.reconciled ? "✓ Reconciled" : "⧯ Unreconciled"}
                    </Badge>
                  </td>
                  <td>
                    <Button variant="ghost" size="sm" onClick={() => onEdit(e)}>Edit</Button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}

function QuickStats({ entries }) {
  const posted = entries.filter(e => e.posted).length;
  const reconciled = entries.filter(e => e.reconciled).length;
  const totalDebits = entries.reduce((sum, e) => sum + e.lines.reduce((s, l) => s + (l.debit || 0), 0), 0);

  return (
    <div className="journ-stats">
      <div className="journ-stat-card">
        <div className="journ-stat-label">Total posted</div>
        <div className="journ-stat-value">{posted}</div>
      </div>
      <div className="journ-stat-card">
        <div className="journ-stat-label">Reconciled</div>
        <div className="journ-stat-value">{reconciled} / {posted}</div>
      </div>
      <div className="journ-stat-card">
        <div className="journ-stat-label">Total debits</div>
        <div className="journ-stat-value">${totalDebits.toFixed(2)}</div>
      </div>
    </div>
  );
}

/* ── Main workstation ── */
function JournalingWorkstationScreen() {
  const [entries, setEntries] = useState(SEED_ENTRIES);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingEntry, setEditingEntry] = useState(null);
  const [tab, setTab] = useState("entries");

  const handlePost = (data) => {
    const newEntry = {
      id: Math.max(...entries.map(e => e.id), 0) + 1,
      date: data.header.date,
      ref: data.header.ref,
      memo: data.header.memo,
      lines: data.lines,
      posted: true,
      reconciled: false,
    };
    setEntries([...entries, newEntry]);
    window.MeridianToast?.success?.("Entry posted", newEntry.ref);
  };

  const handleEdit = (entry) => {
    setEditingEntry(entry);
    setModalOpen(true);
  };

  return (
    <React.Fragment>
      <div className="journ-layout">
        <WorkstationTopbar moduleLabel="Journaling" environment="PAPER" clock="14:32:08 UTC" brandSrc="../../assets/brand/meridian-mark-light.svg" />

        <div className="journ-main">
          <NavRail
            activeId="journal"
            onSelect={() => {}}
            sections={[
              { label: "Journal", items: [
                { id: "entries", label: "Entries", icon: "../../assets/icons/data-operations.svg" },
                { id: "reconcile", label: "Reconciliation", icon: "../../assets/icons/archive-health.svg" },
              ]},
            ]}
          />

          <main className="journ-content">
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
              <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Journaling</h1>
              <Button variant="primary" size="sm" onClick={() => { setEditingEntry(null); setModalOpen(true); }}>
                New entry
              </Button>
            </div>

            <div style={{ display: "flex", gap: 8, borderBottom: "1px solid var(--border)", marginBottom: 12 }}>
              <button onClick={() => setTab("entries")}
                style={{ padding: "9px 14px", background: tab === "entries" ? "transparent" : "transparent", borderBottom: tab === "entries" ? "2px solid var(--accent)" : "none", cursor: "pointer", color: tab === "entries" ? "var(--text-primary)" : "var(--text-secondary)", fontWeight: tab === "entries" ? 600 : 400, border: "none", marginBottom: "-1px" }}>
                Entries
              </button>
              <button onClick={() => setTab("reconcile")}
                style={{ padding: "9px 14px", background: tab === "reconcile" ? "transparent" : "transparent", borderBottom: tab === "reconcile" ? "2px solid var(--accent)" : "none", cursor: "pointer", color: tab === "reconcile" ? "var(--text-primary)" : "var(--text-secondary)", fontWeight: tab === "reconcile" ? 600 : 400, border: "none", marginBottom: "-1px" }}>
                Reconciliation
              </button>
            </div>

            {tab === "entries" && (
              <>
                <QuickStats entries={entries} />
                <PostingHistory entries={entries} onEdit={handleEdit} onReconcile={() => setTab("reconcile")} />
              </>
            )}

            {tab === "reconcile" && (
              <div className="journ-section">
                <div className="journ-section-head">
                  <Eyebrow>Tie unreconciled entries to statement items</Eyebrow>
                </div>
                <p style={{ fontSize: 12, color: "var(--text-muted)" }}>Select entries and statement items to mark as matched.</p>
              </div>
            )}
          </main>
        </div>

        <StatusBar items={[
          { label: "Entries", value: entries.length + " posted" },
          { label: "Reconciled", value: entries.filter(e => e.reconciled).length + " / " + entries.length },
          { label: "Period", value: "2026-Q2 open", push: true },
        ]} />

        <JournalEntryModal open={modalOpen} onClose={() => setModalOpen(false)} accounts={ACCOUNTS} onPost={handlePost} entry={editingEntry} />
      </div>
      <ToastProvider />
    </React.Fragment>
  );
}

window.JournalingWorkstationScreen = JournalingWorkstationScreen;
if (typeof module !== "undefined") module.exports = { JournalingWorkstationScreen };
