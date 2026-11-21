import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function LiveAuthorizationRevokeForm({ authorizationId, disabled, revoke }: {
  authorizationId: string;
  disabled: boolean;
  revoke: (id: string, reason: string) => Promise<void>;
}) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  return (
    <details className="my-2 font-sans">
      <summary className="cursor-pointer font-medium">Revoke live authorization</summary>
      <form className="mt-2 grid gap-2" onSubmit={async (event) => {
        event.preventDefault();
        if (busy || disabled || !reason.trim()) return;
        setBusy(true);
        setError(null);
        try {
          await revoke(authorizationId, reason.trim());
        } catch (failure) {
          setError(failure instanceof Error ? failure.message : "Revocation could not be confirmed.");
        } finally {
          setBusy(false);
        }
      }}>
        <p className="text-muted-foreground">Blocks new orders and replacements for this run.</p>
        <label className="grid gap-1">
          Revocation reason
          <Input required disabled={busy || disabled} value={reason} onChange={(event) => setReason(event.target.value)} />
        </label>
        <Button type="submit" size="sm" variant="destructive" className="justify-self-start"
          disabled={disabled || !reason.trim()} busy={busy} busyLabel="Revoking authorization">
          Confirm revocation
        </Button>
        {error && <p role="alert" className="text-danger">{error}</p>}
      </form>
    </details>
  );
}
