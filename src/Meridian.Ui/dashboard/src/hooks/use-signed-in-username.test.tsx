import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as authApi from "@/lib/api/auth.api";
import { resetSignedInUsernameCache, useSignedInUsername } from "./use-signed-in-username";

function Probe({ id }: { id: string }) {
  const username = useSignedInUsername();
  return <p data-testid={id}>{username ?? "unknown"}</p>;
}

describe("useSignedInUsername", () => {
  beforeEach(() => resetSignedInUsernameCache());
  afterEach(() => vi.restoreAllMocks());

  it("reads the signed-in operator once and shares it across screens", async () => {
    const read = vi.spyOn(authApi, "getSignedInUsername").mockResolvedValue("dana.ops");

    render(
      <>
        <Probe id="a" />
        <Probe id="b" />
      </>
    );

    await waitFor(() => expect(screen.getByTestId("a")).toHaveTextContent("dana.ops"));
    expect(screen.getByTestId("b")).toHaveTextContent("dana.ops");
    expect(read).toHaveBeenCalledTimes(1);
  });

  it("stays unknown without a named session", async () => {
    const read = vi.spyOn(authApi, "getSignedInUsername").mockResolvedValue(null);

    render(<Probe id="a" />);

    await waitFor(() => expect(read).toHaveBeenCalled());
    expect(screen.getByTestId("a")).toHaveTextContent("unknown");
  });

  it("retries after a failed read instead of caching nobody", async () => {
    const read = vi
      .spyOn(authApi, "getSignedInUsername")
      .mockRejectedValueOnce(new Error("network down"))
      .mockResolvedValueOnce("dana.ops");

    const { unmount } = render(<Probe id="a" />);
    await waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId("a")).toHaveTextContent("unknown");
    unmount();

    render(<Probe id="b" />);
    await waitFor(() => expect(screen.getByTestId("b")).toHaveTextContent("dana.ops"));
    expect(read).toHaveBeenCalledTimes(2);
  });
});
