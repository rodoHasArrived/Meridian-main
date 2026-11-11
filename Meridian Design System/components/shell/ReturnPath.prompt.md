Use `ReturnPath` to close an object handoff: an accounting entry can return to its
originating break, or a period review to its account. Feed it only relationships
that the selected object actually carries.

```jsx
<ReturnPath label="Related" layout="stack"
  loops={[{ to: "break", id: "SYN-B17", verb: "Open originating break",
    href: "reconciliation.html?break=SYN-B17", open: () => openBreak("SYN-B17") }]} />
```

A real `href` preserves middle-click, modifier-click, and open-in-new-tab behavior.
An optional `open` callback intercepts only an ordinary primary click. Without a
URL the item is a native button and needs a working `open` callback. Never render
a relationship as actionable when no navigation route exists.

Empty loops render nothing; pass `emptyText` only when the absence itself matters.
The component does not derive relationships, fetch an object, verify permissions,
or preserve unsaved edits. The parent must handle those navigation consequences
and carry stable object identity in URLs or state.

Links support Tab/Enter and buttons support Tab/Enter/Space; the shared focus token
provides the visible ring. Keep verbs meaningful without the arrow or color, and
use `showIds` when an identifier helps the operator verify the return target.
