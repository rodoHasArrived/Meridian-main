Field label (rendered as small-caps)

The wrapper clones one control element with a stable ID, required state, validation and
feedback description. The child must forward ID and ARIA props to its focusable input.
An explicit child ID is preserved; existing description IDs remain associated alongside
the visible hint/error. Use independent FormField wrappers for independent controls.
