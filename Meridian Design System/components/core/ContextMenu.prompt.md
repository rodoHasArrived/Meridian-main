ContextMenu — right-click menu for table rows and UI elements.

Supports icons, dividers, disabled items, and danger (red) styling. Smart positioning: auto-adjusts to avoid overflow off-screen.

```jsx
const [menu, setMenu] = React.useState(null);
<div onContextMenu={(e) => { e.preventDefault(); setMenu({ x: e.clientX, y: e.clientY }); }}>
  Right-click me
</div>
{menu && (
  <ContextMenu
    x={menu.x}
    y={menu.y}
    items={[
      { label: 'Edit', icon: '✎', onClick: handleEdit },
      { label: 'Copy', icon: '⎘', onClick: handleCopy },
      { type: 'divider' },
      { label: 'Delete', icon: '⌫', dangerous: true, onClick: handleDelete }
    ]}
    onClose={() => setMenu(null)}
  />
)}
```

Manage the anchor point yourself (as above) and pass it as `x`/`y` — same ownership model as `Modal`'s `open`. Arrow keys move between items, Escape closes, and the first enabled item takes focus on open.
