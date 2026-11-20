import { useCallback, useState } from "react";

/** A row clicked in the current view can override a linked selection until the next linked selection. */
export function useLinkedRecordSelection<T extends string = string>(requested: string | null) {
  const [selection, setSelection] = useState<{ requested: string | null; id: T } | null>(null);
  const select = useCallback((id: T) => setSelection({ requested, id }), [requested]);
  return [selection?.requested === requested ? selection.id : null, select] as const;
}
