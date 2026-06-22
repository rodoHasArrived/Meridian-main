Action button mirroring the desktop control styles. `primary` is the solid teal-blue (`--accent`) institutional action — **one per screen**; `ghost` is the white paper secondary with a hairline border; `danger` is a ghost that resolves to red on hover; `link` is a text-only info action.

```jsx
<Button variant="primary">Run backfill</Button>
<Button variant="ghost">Export</Button>
<Button variant="danger">Halt session</Button>
<Button variant="link">View run ledger</Button>
<Button busy busyLabel="Running…">Run</Button>
```

6px radius, 13px Segoe; primary is SemiBold white, ghost is Normal weight. Sizes `sm | default | icon`. No glow, no offset shadow — hover/press shift background + border only.
