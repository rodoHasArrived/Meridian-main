Slot-based workstation frame — one component owns the full-viewport chrome scaffolding (topbar / nav rail / scrolling main / optional right rail / status bar) and renders the `<main>` landmark for you. All slots optional; main and aside are crash-isolated so a failing panel renders an evidence strip instead of blanking the chrome. Declare the workspace archetype here (`archetype`) and name the main region from its own `h1` (`labelledBy`).

```jsx
<WorkstationShell
  archetype="reconcile"
  labelledBy="workspace-title"
  topbar={<WorkstationTopbar moduleLabel="Trading Desk" environment="LIVE" />}
  nav={<NavRail sections={sections} activeId="trading" onSelect={go} />}
  statusBar={<StatusBar items={telemetry} />}
  aside={<OrderTicket …/>} asideWidth={340} asideLabel="Order ticket"
>
  <h1 id="workspace-title">Blotter</h1>
  <Blotter rows={orders} />
</WorkstationShell>
```

This is the only sanctioned shell: never hand-roll `display:flex` chrome scaffolding or a second `<main>` in a template. Omitting `nav`/`aside`/`statusBar` collapses those regions cleanly; the `100vh` default fills the page, `height="100%"` fits an embedded mount.
