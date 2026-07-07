Session & permission primitives — the pieces every real deployment needs and no template previously showed.

`UserMenu` — initials chip (chrome-dark by default; `onChrome={false}` for paper surfaces) opening a popover with identity, role badge, custom items, and sign out. `RoleBadge` — small-caps permission chip: admin (violet) · operator (steel) · viewer (neutral). `ReadOnlyBanner` — persistent amber strip when the operator's role can't write. Use this, not a Toast — permission state is not transient.

```jsx
<UserMenu name="R. Alvarez" role="operator" detail="desk-2 · UTC"
  items={[{ label: "Preferences", onSelect: openPrefs }]} onSignOut={signOut} />
<RoleBadge role="viewer" />
<ReadOnlyBanner />
```
