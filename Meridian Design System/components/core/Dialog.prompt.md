Compound dialog — the ONE overlay in the system: `Dialog` (scrim + ESC + focus trap via the shared overlay hook) with `DialogHeader` / `DialogBody` / `DialogFooter` children, plus `DialogForm` for a bounded create/edit flow. Width is a prop, not a component: `size="sm"` (420px, confirmations) · `"md"` (520px, default) · `"lg"` (720px, form takeover). Common mistakes: rendering it always-open without `open`, nesting a second dialog (never stack overlays), or reaching for the deprecated `Modal*` alias.

```jsx
<Dialog open={open} onClose={close} title="Close position" size="sm">
  <DialogBody>Sell 400 AAPL at market?</DialogBody>
  <DialogFooter>
    <Button variant="ghost" onClick={close}>Cancel</Button>
    <Button variant="danger" onClick={confirm}>Close position</Button>
  </DialogFooter>
</Dialog>

<DialogForm open={editing} onClose={close} title="New journal entry"
  size="lg" submitLabel="Create entry" onSubmit={save}>
  <FormGrid cols={2}>…</FormGrid>
</DialogForm>
```

Focus moves into the panel on open, Tab is trapped at the boundaries, and focus restores to the trigger on close — don't add your own focus code.
