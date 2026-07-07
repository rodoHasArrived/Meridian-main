Compound modal — `Modal` (overlay + ESC + focus trap via the shared overlay hook) with `ModalHeader` / `ModalBody` / `ModalFooter` children. For simple confirmations prefer `Dialog`, which composes the same pieces for you. Common mistakes: rendering it always-open without `open`, or nesting a second Modal — never stack overlays.

```jsx
<Modal open={open} onClose={() => setOpen(false)}>
  <ModalHeader title="Close position" onClose={() => setOpen(false)} />
  <ModalBody>Sell 400 AAPL at market?</ModalBody>
  <ModalFooter>
    <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
    <Button variant="danger" onClick={confirm}>Close position</Button>
  </ModalFooter>
</Modal>
```

Focus moves into the panel on open, Tab is trapped at the boundaries, and focus restores to the trigger on close — don't add your own focus code.
