OptimisticCell — EditableCell's write counterpart. A committed edit shows as Provisional (dashed, struck prior beside it) until the provider's `commit` resolves; a throw rejects it and the value falls back WITH the reason shown, never silently. Confirmed edits show `Revert…`, which demands a reason; `OptimisticRowBar` tallies a row's or a selection's edits and reverts every confirmed one under a single reason. Wrap in `OptimisticProvider({commit, revert, onSettle})`; without one the cell keeps a private ledger and rejects edits with an explicit missing-write-handler reason. A confirmed receipt requires a configured commit handler to resolve; a revert requires a configured compensation handler and a nonblank reason. UI callbacks alone never certify submission.

Batch compensation unwinds newest first; keep that order when applying `prior` values in a revert handler or `onSettle`. Entries beneath a newer provisional/reverting write, or a newer confirmed write outside the selected batch, wait for that write to settle or be reverted first. This prevents an older undo from overwriting a newer value on the same field.

Transport outcomes own the ledger state. `onSettle`, `onChange`, and subscriptions
are observers: synchronous errors and rejected promises are reported through
`globalThis.reportError` (`console.error` fallback). Notifications are not awaited;
a fault cannot turn a confirmed write into a rejection, undo a successful
compensation, or stop other subscribers or later batch notifications. Transport
failures retain their original rejection/revert reason. A callback failure is not
evidence that the write failed: repair or reload a failed consumer projection
from its authoritative source, and monitor the host error channel for faults.
