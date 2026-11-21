import { useCallback, useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PanelReload } from "@/components/ui/panel-reload";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { apiGetJson, apiPutJson } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type { ConfigurationChangeStatus } from "@/screens/settings-change-impact";

const endpoint = "/api/config/alpaca/feed";
const feeds = [
  { value: "iex", label: "IEX (single venue)" },
  { value: "sip", label: "SIP (consolidated US equities)" },
  { value: "delayed_sip", label: "Delayed SIP (consolidated US equities)" }
];

export function AlpacaFeedRegion() {
  const [savedFeed, setSavedFeed] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const active = useRef<AbortController | null>(null);

  const read = useCallback(async () => {
    if (active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setError(null);
    try {
      const result = await apiGetJson<{ feed: string }>(endpoint, { signal: controller.signal, allowDevelopmentFallback: false });
      if (!controller.signal.aborted) {
        setSavedFeed(result.feed);
        setDraft(result.feed);
      }
    } catch (cause) {
      if (!controller.signal.aborted) setError(describeApiError(cause, "Alpaca feed settings are unavailable.").summary);
    } finally {
      if (active.current === controller) {
        active.current = null;
        setBusy(false);
      }
    }
  }, []);

  useEffect(() => {
    void read();
    return () => { active.current?.abort(); active.current = null; };
  }, [read]);

  async function save() {
    if (active.current || !feeds.some(feed => feed.value === draft) || draft === savedFeed) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const status = await apiPutJson<ConfigurationChangeStatus>(endpoint, { feed: draft },
        { signal: controller.signal, allowDevelopmentFallback: false });
      if (!controller.signal.aborted) {
        setSavedFeed(draft);
        setNotice(status.requiresRestart ? "Feed saved. Restart the host to activate it." : "Feed saved.");
      }
    } catch (cause) {
      if (!controller.signal.aborted) setError(describeApiError(cause, "The Alpaca feed was not saved.").summary);
    } finally {
      if (active.current === controller) {
        active.current = null;
        setBusy(false);
      }
    }
  }

  return <Card>
    <CardHeader className="flex-row items-start justify-between gap-3">
      <div>
        <CardTitle>Alpaca equity feed</CardTitle>
        <CardDescription>This choice applies to Alpaca on this host after restart. SIP requires account verification.</CardDescription>
      </div>
      <PanelReload reload={read} failed={Boolean(error) && savedFeed === null} busy={busy} what="Alpaca feed settings" />
    </CardHeader>
    <CardContent className="space-y-3">
      {savedFeed !== null ? <div className="flex flex-wrap items-end gap-2">
        <label className="grid gap-1 text-sm">Equity feed
          <Select value={draft} disabled={busy} onChange={event => { setDraft(event.target.value); setNotice(null); setError(null); }}>
            {feeds.some(feed => feed.value === savedFeed) ? null : <option value={savedFeed}>{savedFeed} (saved)</option>}
            {feeds.map(feed => <option key={feed.value} value={feed.value}>{feed.label}</option>)}
          </Select>
        </label>
        <Button size="sm" onClick={() => void save()} disabled={busy || savedFeed === draft}>
          {draft === "sip" ? "Verify SIP and save" : "Save feed"}
        </Button>
      </div> : busy ? <p role="status" className="text-sm">Loading Alpaca feed settings…</p> : null}
      {error ? <StatusBanner tone="danger" title={savedFeed === null ? "Alpaca feed unavailable" : "Feed change refused"} detail={error} /> : null}
      {notice ? <p role="status" className="text-sm">{notice}</p> : null}
    </CardContent>
  </Card>;
}
