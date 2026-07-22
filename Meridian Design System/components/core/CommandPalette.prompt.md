CommandPalette — the Ctrl-K operator command surface the WorkstationTopbar advertises. Grouped, keyboard-first results: a 4px left-inset accent marks the selected row (no full background swap), shortcuts render as mono `kbd`s, every group carries a small-caps header, and the list scrolls past ~6 rows. The only elevation is the float shadow.

Pair with `useCommandPalette()`, which wires the global Ctrl/Cmd+K toggle: const { open, setOpen, closePalette } = useCommandPalette(); <CommandPalette open={open} onClose={closePalette} commands={commands} />

Or use it self-contained — it registers its own `hotkey` (default Ctrl/Cmd+K) and manages open state internally, so the lone requirement is the command list: <CommandPalette commands={commands} />
