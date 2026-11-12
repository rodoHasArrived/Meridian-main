Use `GlobalSearch` for a masthead typeahead over the current screen's objects and
real destinations. The canonical template scaffold already mounts it; avoid a
second overlay or duplicate keyboard shortcut.

```jsx
<GlobalSearch open={searchOpen} onClose={() => setSearchOpen(false)}
  scopeLabel="Synthetic accounts"
  resolve={query => accounts.filter(a => a.label.includes(query)).map(a => ({
    id: a.id, label: a.label, meta: a.id, group: "Accounts", run: () => openAccount(a.id)
  }))}
  destinations={[{ id: "reports", label: "Reporting", run: openReporting }]} />
```

`resolve` is synchronous and scoped to authorized objects; never use the overlay
to expose data the current role cannot read. The parent supplies real navigation
callbacks. Empty query shows destinations; a nonempty query combines matching
objects with at most eight matching destinations. Resolver exceptions currently
become an empty result set; surface consequential source failures separately.
No network, persistence, ranking service, or authorization enforcement is built in.

Opening focuses the input. Up/Down and Ctrl+N/Ctrl+P change the active result;
Enter activates it and Escape requests close. Clicking the scrim also closes.
Validate focus containment and return focus in the consuming shell: the component
has no shared overlay focus-trap/restoration hook. The group headings and input
must remain readable at workstation zoom and with long object labels.
