import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AsyncRegion } from "@/components/ui/async-region";

const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});

afterEach(() => {
  consoleError.mockClear();
});

const skeleton = <div data-testid="skeleton">shimmer</div>;

describe("AsyncRegion", () => {
  it("shows the skeleton on first load and announces it", () => {
    render(
      <AsyncRegion loading hasData={false} label="reconciliation table" skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.getByTestId("skeleton")).toBeInTheDocument();
    expect(screen.queryByText("resolved")).toBeNull();
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(status).toHaveTextContent("Loading reconciliation table…");
  });

  it("keeps data on screen during a background refresh instead of blanking it", () => {
    render(
      <AsyncRegion loading hasData label="table" skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.queryByTestId("skeleton")).toBeNull();
    expect(screen.getByText("resolved")).toBeInTheDocument();
  });

  it("renders resolved content once settled", () => {
    render(
      <AsyncRegion loading={false} hasData skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.getByText("resolved")).toBeInTheDocument();
  });

  it("shows the error state with retry when first load fails", async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    render(
      <AsyncRegion
        loading={false}
        error="Close package could not be loaded."
        hasData={false}
        onRetry={onRetry}
        skeleton={skeleton}
      >
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Close package could not be loaded.");
    expect(screen.queryByText("resolved")).toBeNull();
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("keeps stale data visible when a refresh errors but data already exists", () => {
    render(
      <AsyncRegion loading={false} error="refresh failed" hasData skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByText("resolved")).toBeInTheDocument();
  });

  it("reads a request-lifecycle phase directly", () => {
    render(
      <AsyncRegion phase="running" hasData={false} label="feed" skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.getByTestId("skeleton")).toBeInTheDocument();
  });

  it("renders the empty node when settled with no data", () => {
    render(
      <AsyncRegion loading={false} hasData={false} empty={<div>nothing here</div>} skeleton={skeleton}>
        <div>resolved</div>
      </AsyncRegion>
    );

    expect(screen.getByText("nothing here")).toBeInTheDocument();
    expect(screen.queryByText("resolved")).toBeNull();
  });

  it("contains a render-time throw from resolved children to this region", () => {
    function Boom(): JSX.Element {
      throw new Error("child exploded");
    }

    render(
      <AsyncRegion loading={false} hasData label="detail panel" skeleton={skeleton}>
        <Boom />
      </AsyncRegion>
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Detail panel hit an unexpected error.");
  });
});
