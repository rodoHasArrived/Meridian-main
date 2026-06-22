Left operator rail — 14rem light paper sidebar (`#F4F6F8`), small-caps section labels, nav items with a **3px teal-blue left indicator** + light wash on the active item.

```jsx
<NavRail
  activeId="security-master"
  onSelect={setRoute}
  sections={[
    { label: "Data", items: [
      { id: "security-master", label: "Security Master", icon: "assets/icons/security-master.svg", shortcut: "G S" },
      { id: "data-browser", label: "Data Browser", icon: "assets/icons/data-browser.svg" },
    ]},
  ]}
/>
```

Icons come from `assets/icons/` (duotone set). Active = `--sidebar-active` wash + blue left bar; hover = `--sidebar-hover`.
