Show the source-document excerpt behind a selected value without replacing the reconciliation or analysis workspace. Include title/source, page/section, highlighted field, as-of/received dates, completeness, confidence, and open-original action.

```jsx
<EvidencePreview title="Custodian cash statement" source="NT-INV-20260731.pdf"
  page="14" before={before} highlight="USD 142.18" after={after} confidence={98}/>
```

Keep evidence text selectable and screen-reader readable; do not rely on an image-only crop.

`condition="restricted"` withholds excerpt content and derived metadata and disables
opening/comparing it. `redacted` states that the supplied excerpt already contains
redactions; it does not remove arbitrary supplied text. Supply only authorized
source identities/citations and enforce permissions on the server.
