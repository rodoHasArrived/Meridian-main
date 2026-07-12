// Journal entry drawer — DS Drawer wrapping JournalEntryForm (balance-gated Post) plus
// supporting-document and note fields. Seeds arrive prefilled for accrual / FX-reval /
// post-to-account flows; `seedKey` re-keys the form so each open starts fresh.
function AcctJournalDrawer(props) {
  if (!props.open) return null;
  const DS = window.MeridianDesignSystem_4f61be;
  if (!DS) return null;
  const { Drawer, DrawerBody, JournalEntryForm, FileUpload, TextArea, Callout, Eyebrow } = DS;

  return (
    <Drawer open={props.open} onClose={props.onClose} title="New journal entry" size="700px">
      <DrawerBody>
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          {props.note && (
            <Callout tone="info" title={props.noteTitle}>{props.note}</Callout>
          )}
          <JournalEntryForm
            key={props.seedKey}
            currency="USD"
            accounts={props.accounts || []}
            initialHeader={props.seedHeader}
            initialLines={props.seedLines}
            onPost={props.onPost}
          />
          <div style={{ borderTop: "1px solid var(--border)", paddingTop: 14, display: "flex", flexDirection: "column", gap: 14 }}>
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              <Eyebrow>Supporting documents</Eyebrow>
              <FileUpload label="Attach receipts, invoices, or statements" multiple accept=".pdf,.xlsx,.csv" />
            </div>
            <TextArea label="Internal notes (optional)"
              placeholder="e.g. authorization, dispute reference, settlement terms…" rows={3} />
          </div>
          <p style={{ margin: 0, fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
            Post is enabled once Σ debit = Σ credit. Posting appends the lines to the general ledger.
          </p>
        </div>
      </DrawerBody>
    </Drawer>
  );
}

window.AcctJournalDrawer = AcctJournalDrawer;
if (typeof module !== "undefined") module.exports = { AcctJournalDrawer };
