import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { ErrorNotice } from "@/components/ui/error-notice";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";

const path = "/api/ledger/journal-automation/recurring/occurrences?fundProfileId=default-fund&ledgerBookId=book-1";

describe("ErrorNotice", () => {
  it("shows a plain sentence and keeps the request path collapsed under Technical details", async () => {
    const onRetry = vi.fn();
    const { container } = render(
      <ErrorNotice
        error={createApiErrorFromResponseBody(path, 503, "", "corr-42")}
        title="Recurring journals could not be loaded."
        fallback="The retained occurrence state could not be read."
        onRetry={onRetry}
      />
    );

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Recurring journals could not be loaded.");
    expect(alert).toHaveTextContent("The retained occurrence state could not be read.");

    // The disclosure is closed by default, so the path is in the DOM but not in the reading path.
    const disclosure = alert.querySelector("details") as HTMLDetailsElement;
    expect(disclosure.open).toBe(false);
    expect(within(disclosure).getByText("Technical details")).toBeInTheDocument();
    expect(within(disclosure).getByText(path)).toBeInTheDocument();
    expect(within(disclosure).getByText("corr-42")).toBeInTheDocument();
    const readingPath = Array.from(alert.children).filter((child) => child.tagName !== "DETAILS");
    expect(readingPath.map((child) => child.textContent).join(" ")).not.toMatch(/\/api\/|503/);

    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
    expect((await axe(container)).violations).toEqual([]);
  });

  it("does not offer Retry when the service rejected the request", () => {
    render(
      <ErrorNotice
        error={createApiErrorFromResponseBody("/api/export/analysis", 403, "")}
        title="The export could not be started."
        onRetry={vi.fn()}
      />
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Permission denied for this Meridian role.");
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });

  it("shows a plain string reason without technical details", () => {
    render(<ErrorNotice error="Durable occurrence store unavailable" title="Recurring journals could not be loaded." />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Durable occurrence store unavailable");
    expect(alert.querySelector("details")).toBeNull();
  });
});
