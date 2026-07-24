Path trail for nested navigation — registry → dataset → field, or workspace → module → detail. Terse labels, click to jump up the hierarchy.

```jsx
<Breadcrumb items={[
  { label: "Registry", onClick: () => go("registry") },
  { label: "Equities", onClick: () => go("equities") },
  { label: "AAPL" },
]} />
```

The last item is the current location — give it no `onClick`. Keep labels short; this is orientation, not a page title.
