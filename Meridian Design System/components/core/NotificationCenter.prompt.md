NotificationCenter — persistent notification rail: a bell button with an unread count and an anchored panel (tone dot, title, detail, relative time). The durable complement to Toast: toasts confirm actions and vanish; this answers "what fired while I was away". Controlled — you own the items array.

```jsx
<NotificationCenter
  items={[{ id: "n1", tone: "error", title: "Drawdown breach", detail: "momentum.v4 · -8.4%", time: firedAt, read: false }]}
  onMarkAllRead={() => setAll(read)}
  onSelect={(n) => nav(n.href)} />
```
