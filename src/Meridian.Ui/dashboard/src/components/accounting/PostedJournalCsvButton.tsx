import { useEffect, useRef, useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { describeApiError } from "@/lib/api-errors";
import { exportLedgerPeriodJournalCsv } from "@/lib/ledger-reports-api";

export function PostedJournalCsvButton({ periodId, bookId }: { periodId: string | null; bookId: string | null }) {
  const scope = periodId && bookId ? `${bookId}:${periodId}` : null;
  const currentScope = useRef(scope);
  currentScope.current = scope;
  const pending = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    setBusy(false); setMessage(null);
    return () => { pending.current?.abort(); pending.current = null; };
  }, [scope]);

  async function download() {
    if (!periodId || !scope || pending.current) return;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true); setMessage(null);
    try {
      const content = await exportLedgerPeriodJournalCsv(periodId, { signal: controller.signal });
      if (controller.signal.aborted || currentScope.current !== scope) return;
      const url = URL.createObjectURL(content);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `journal-${periodId}.csv`;
      anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      setMessage("Complete period journal downloaded. Table filters do not limit this file.");
    } catch (error) {
      if (!controller.signal.aborted && currentScope.current === scope)
        setMessage(describeApiError(error, "Journal CSV could not be downloaded.").summary);
    } finally {
      if (pending.current === controller) { pending.current = null; setBusy(false); }
    }
  }
  return <div className="grid justify-items-start gap-1">
    <Button type="button" size="sm" variant="outline" disabled={!scope || busy} onClick={() => void download()}
      title="Download complete posted journals for the selected period, including entries outside the table filters.">
      <Download className="mr-2 h-4 w-4" aria-hidden="true" />{busy ? "Downloading journal…" : "Download journal CSV"}
    </Button>
    {message ? <p role="status" className="text-xs text-muted-foreground">{message}</p> : null}
  </div>;
}
