Form layout primitives — structure only, no state. `FormRow` labels a field (optional `hint`/`error`, `horizontal`, `span`); `FormGrid` arranges rows in 1–3 columns; `FormDivider` and `FormSectionLabel` break a long form into sections.

```jsx
<FormGrid cols={2}>
  <FormRow label="Symbol" hint="Ticker or CUSIP"><Input value={s} onChange={setS} /></FormRow>
  <FormRow label="Threshold" error={err}><NumberInput value={t} onChange={setT} /></FormRow>
  <FormSectionLabel>Delivery</FormSectionLabel>
  <FormRow label="Recipients" span={2}><TagInput tags={r} onChange={setR} /></FormRow>
</FormGrid>
```

Pair with `FormValidation` for the rules and error strings. One field per `FormRow`; use `span={2}` for a wide input across a 2-column grid.
