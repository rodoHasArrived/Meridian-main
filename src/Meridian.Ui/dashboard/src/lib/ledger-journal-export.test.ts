import { exportLedgerPeriodJournalCsv } from "./ledger-reports-api";

afterEach(() => { vi.unstubAllGlobals(); document.cookie = "mdc-csrf=; Max-Age=0; path=/"; });

it("uses the existing period export route and authenticated CSRF headers, preserving returned bytes", async () => {
  const bytes = new Blob(["exact server journal"]);
  const fetcher = vi.fn().mockResolvedValue({ ok: true, blob: () => Promise.resolve(bytes) });
  vi.stubGlobal("fetch", fetcher);
  document.cookie = "mdc-csrf=synthetic-csrf; path=/";
  const result = await exportLedgerPeriodJournalCsv("11111111-1111-1111-1111-111111111111");
  expect(result).toBe(bytes);
  expect(fetcher).toHaveBeenCalledWith("/api/ledger/periods/11111111-1111-1111-1111-111111111111/journal-export",
    expect.objectContaining({ method: "POST", body: '{"format":"csv"}', headers: expect.objectContaining({ "X-CSRF-Token": "synthetic-csrf", "Content-Type": "application/json" }) }));
});

it("propagates a server refusal without a fixture or local table export", async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response('{"error":"Export permission required"}', { status: 403, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetcher);
  await expect(exportLedgerPeriodJournalCsv("period-a")).rejects.toMatchObject({ status: 403 });
  expect(fetcher).toHaveBeenCalledOnce();
});
