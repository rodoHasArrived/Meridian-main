import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BackfillBatchReceipts } from "@/screens/data-screen.backfill-batch-receipts";
import { renderWithRouter } from "@/test/render";
import type { BackfillBatchReceipt, BackfillBatchReceiptSummary } from "@/types";

function receipt(revisionId: string, servingProvider: string, rowCount: number): BackfillBatchReceipt {
  return {
    revisionId, servingProvider, rowCount, instrument: "AAPL", requestedProvider: "composite", feed: "daily",
    granularity: "daily", from: "2026-10-01", to: "2026-10-07", status: "partial",
    contentHash: "abc123", calendar: null, expectedSessions: null, receivedSessions: []
  };
}

describe("Backfill provider attribution", () => {
  it("shows retained counts for both providers and counts each revision once", () => {
    const first = receipt("p1", "polygon", 4);
    renderWithRouter(<BackfillBatchReceipts receipts={[first, first, receipt("p2", "polygon", 3), receipt("s1", "stooq", 2)]}
      attempts={[{ symbol: "AAPL", providerId: "polygon", reason: "rate-limited", bars: 0, attempted: true }]} />);

    const table = within(screen.getByRole("table", { name: "Backfill bars by source" }));
    expect(table.getByRole("row", { name: "AAPL polygon 7" })).toBeVisible();
    expect(table.getByRole("row", { name: "AAPL stooq 2" })).toBeVisible();
    expect(screen.getByText("Provider attempts (1)")).toBeVisible();
    expect(screen.getByText(/Rate limited/)).toBeInTheDocument();
  });

  it("aggregates compact receipts from queued runs", () => {
    const { expectedSessions, receivedSessions, ...retained } = receipt("queued", "tiingo", 3);
    const summary: BackfillBatchReceiptSummary = {
      ...retained, expectedSessionCount: expectedSessions?.length ?? null, receivedSessionCount: receivedSessions.length
    };
    renderWithRouter(<BackfillBatchReceipts receipts={[summary]} />);
    expect(screen.getByRole("row", { name: "AAPL tiingo 3" })).toBeVisible();
  });
});
