import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RegionErrorBoundary, RegionErrorState } from "@/components/ui/error-boundary";

const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});

afterEach(() => {
  consoleError.mockClear();
});

function Boom({ shouldThrow }: { shouldThrow: boolean }): JSX.Element {
  if (shouldThrow) {
    throw new Error("panel exploded");
  }

  return <div>panel content</div>;
}

describe("RegionErrorState", () => {
  it("renders as an alert with an optional retry", async () => {
    const onRetry = vi.fn();
    const user = userEvent.setup();
    render(<RegionErrorState message="Table could not be loaded." onRetry={onRetry} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Table could not be loaded.");
    await user.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("omits the retry button when no handler is given", () => {
    render(<RegionErrorState message="No retry here." />);
    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("RegionErrorBoundary", () => {
  it("contains a render-time throw to its own region", () => {
    render(
      <RegionErrorBoundary regionLabel="reconciliation table">
        <Boom shouldThrow />
      </RegionErrorBoundary>
    );

    expect(screen.getByRole("alert")).toHaveTextContent("reconciliation table hit an unexpected error.");
    expect(screen.queryByText("panel content")).toBeNull();
  });

  it("supports a custom fallback with reset", async () => {
    const user = userEvent.setup();

    function Harness(): JSX.Element {
      const [broken, setBroken] = useState(true);
      return (
        <RegionErrorBoundary
          resetKeys={[broken]}
          fallback={({ reset }) => (
            <button
              type="button"
              onClick={() => {
                setBroken(false);
                reset();
              }}
            >
              recover
            </button>
          )}
        >
          <Boom shouldThrow={broken} />
        </RegionErrorBoundary>
      );
    }

    render(<Harness />);
    await user.click(screen.getByRole("button", { name: "recover" }));
    expect(screen.getByText("panel content")).toBeInTheDocument();
  });

  it("reports errors to the onError hook", () => {
    const onError = vi.fn();
    render(
      <RegionErrorBoundary onError={onError}>
        <Boom shouldThrow />
      </RegionErrorBoundary>
    );

    expect(onError).toHaveBeenCalledTimes(1);
    expect(onError.mock.calls[0][0]).toBeInstanceOf(Error);
  });
});
