import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PrincipalIdInput } from "@/components/meridian/principal-id-input";

const fetchMock = vi.fn();

function respondWith(body: unknown) {
  return { ok: true, status: 200, text: async () => JSON.stringify(body) };
}

function optionValues(): string[] {
  const list = screen.queryByTestId("principal-id-options");
  return list ? Array.from(list.querySelectorAll("option")).map((option) => option.value) : [];
}

describe("PrincipalIdInput", () => {
  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("makes no request until the field is focused", () => {
    render(<PrincipalIdInput principalKind="Group" value="" onChange={() => undefined} aria-label="Principal" />);

    expect(fetchMock).not.toHaveBeenCalled();
    expect(optionValues()).toEqual([]);
  });

  it("suggests role and role-profile names for a group principal", async () => {
    fetchMock.mockImplementation(async (url: string) => url.includes("/roles")
      ? respondWith({ roles: [{ role: "Controller", displayName: "Controller" }, { role: "reporting-ops", displayName: "Reporting operations" }], permissions: [] })
      : respondWith([{ username: "alex", role: "Controller", roleProfileName: null }]));
    render(<PrincipalIdInput principalKind="Group" value="" onChange={() => undefined} aria-label="Principal" />);

    fireEvent.focus(screen.getByLabelText("Principal"));

    await waitFor(() => expect(optionValues()).toEqual(["Controller", "reporting-ops"]));
    expect(screen.getByLabelText("Principal")).toHaveAttribute("list", screen.getByTestId("principal-id-options").id);
  });

  it("suggests usernames for a user principal and loads the directory only once", async () => {
    fetchMock.mockImplementation(async (url: string) => url.includes("/roles")
      ? respondWith({ roles: [{ role: "Controller", displayName: "Controller" }], permissions: [] })
      : respondWith([{ username: "alex", role: "Controller" }, { username: "sam", role: "Viewer" }]));
    render(<PrincipalIdInput principalKind="User" value="" onChange={() => undefined} aria-label="Principal" />);

    fireEvent.focus(screen.getByLabelText("Principal"));
    await waitFor(() => expect(optionValues()).toEqual(["alex", "sam"]));
    fireEvent.focus(screen.getByLabelText("Principal"));

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("stays a plain text field when the account list is forbidden or the kind is Company", async () => {
    fetchMock.mockImplementation(async (url: string) => url.includes("/roles")
      ? respondWith({ roles: [{ role: "Controller", displayName: "Controller" }], permissions: [] })
      : { ok: false, status: 403, statusText: "Forbidden", text: async () => "{}" });
    const onChange = vi.fn();
    const { rerender } = render(<PrincipalIdInput principalKind="User" value="" onChange={onChange} aria-label="Principal" />);

    fireEvent.focus(screen.getByLabelText("Principal"));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    fireEvent.change(screen.getByLabelText("Principal"), { target: { value: "typed-by-hand" } });

    expect(onChange).toHaveBeenCalled();
    expect(optionValues()).toEqual([]);
    expect(screen.getByLabelText("Principal")).not.toHaveAttribute("list");

    rerender(<PrincipalIdInput principalKind="Company" value="" onChange={onChange} aria-label="Principal" />);
    expect(optionValues()).toEqual([]);
  });
});
