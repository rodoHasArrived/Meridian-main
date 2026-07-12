Multi-line text field — same paper/border/focus treatment as `Input`, vertically resizable.

```jsx
<TextArea label="Memo" rows={3} value={memo} onChange={(e) => setMemo(e.target.value)} />
<TextArea label="Notes" error="Required before posting" />
```

Omit both `label` and `error` to render a bare `<textarea>` for embedding inside your own `FormField`. Same mono placeholder / hover-darken / focus-ring / red-error behavior as `Input`.
