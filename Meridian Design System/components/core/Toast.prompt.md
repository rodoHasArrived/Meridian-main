Toast notifications — mount `<ToastProvider />` ONCE at the app root, then fire from anywhere via `window.MeridianToast` or the `useToast()` hook. Never render ToastProvider per-screen or per-component; a second provider double-renders every toast.

```jsx
// once, at the root:
<ToastProvider />

// anywhere:
window.MeridianToast.success("Order accepted", "buy 400 AAPL · market · DAY");
window.MeridianToast.error("Reconciliation failed", "RECON-204 · custody cash break");
```

Toasts are for transient confirmations of user actions. Persistent conditions (data gaps, degraded feeds) belong in `StatusBanner` or the status bar — not a toast that disappears.
