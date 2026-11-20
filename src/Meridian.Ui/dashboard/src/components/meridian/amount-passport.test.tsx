import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { AmountPassportProvider, AmountPassportTrigger } from "./amount-passport";
import { amountPassportHref, getAmountPassport, readAmountPassportLink } from "@/lib/amount-passport-api";
import { ApiError } from "@/lib/api-errors";
import { createAmountPassport, passportIdentity } from "@/test/amount-passport-fixtures";
import { TestMemoryRouter } from "@/test/render";

vi.mock("@/lib/amount-passport-api", async (importOriginal) => ({
  ...await importOriginal<typeof import("@/lib/amount-passport-api")>(), getAmountPassport: vi.fn()
}));

describe("Amount passport rail", () => {
  beforeEach(() => vi.resetAllMocks());

  it("opens by keyboard, copies a reusable address, preserves the work surface and compares a second period", async () => {
    const before = createAmountPassport();
    const nextIdentity = passportIdentity({ revision: "2", subjectId: "new:line:debit",
      scope: { ...before.reference.identity.scope, periodId: "period-2" } });
    const after = createAmountPassport(nextIdentity, "Cash debit in August", "175");
    vi.mocked(getAmountPassport).mockImplementation(async identity => identity.revision === "1" ? before : after);
    const user = userEvent.setup();
    render(<TestMemoryRouter><AmountPassportProvider enabled scopeKey="scope" session="operator">
      <input aria-label="Filter postings" defaultValue="cash" />
      <AmountPassportTrigger selection={{ label: "July posting", displayValue: "$125.50", reference: before.reference }} />
      <AmountPassportTrigger selection={{ label: "August posting", displayValue: "$175.00", reference: after.reference }} />
    </AmountPassportProvider></TestMemoryRouter>);
    const trigger = screen.getByRole("button", { name: /Inspect July posting/ });
    trigger.focus();
    await user.keyboard("{Enter}");
    const rail = await screen.findByRole("dialog", { name: "Amount passport" });
    await within(rail).findByText("Complete");
    expect(rail).toHaveTextContent("HardClosed");
    expect(rail).toHaveTextContent("View allowed");
    expect(rail).toHaveTextContent("Cannot amend");
    expect(screen.getByRole("textbox", { name: "Filter postings" })).toHaveValue("cash");
    const width = within(rail).getByRole("slider", { name: "Rail width" });
    fireEvent.change(width, { target: { value: "33" } });
    expect(rail.style.width).toBe("33rem");
    const copy = within(rail).getByRole("button", { name: "Copy stable link" });
    copy.focus();
    await user.keyboard("{Enter}");
    await within(rail).findByText("Link copied.");
    const link = new URL(await navigator.clipboard.readText());
    expect(readAmountPassportLink(link.hash)).toEqual(before.reference.identity);
    expect(link.href).not.toContain("July");
    await user.click(within(rail).getByRole("button", { name: "Pin for comparison" }));
    await user.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());

    await user.click(screen.getByRole("button", { name: /Inspect August posting/ }));
    const comparison = await screen.findByRole("region", { name: "Comparison baseline" });
    expect(await within(comparison).findByText("125.50")).toBeVisible();
    expect(screen.getByText("175")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Open full investigation" }));
    expect(screen.getByRole("button", { name: "Return to rail" })).toBeVisible();
    expect((await axe(screen.getByRole("dialog"))).violations).toEqual([]);
  });

  it("reopens a stable link, withholds cached proof after a session change and checks authority again", async () => {
    const proof = createAmountPassport(passportIdentity(), "Private posting name", "7789.75");
    const initialEntries = [amountPassportHref(proof.reference.identity).replace("/workstation", "")];
    vi.mocked(getAmountPassport).mockResolvedValueOnce(proof).mockRejectedValueOnce(new ApiError({ path: "passport", status: 403 }));
    const viewFor = (session: string) => <TestMemoryRouter initialEntries={initialEntries}>
      <AmountPassportProvider enabled scopeKey="same" session={session}><main>Ledger Explorer</main></AmountPassportProvider>
    </TestMemoryRouter>;
    const view = render(viewFor("earlier operator"));
    expect(await screen.findByRole("heading", { name: "Private posting name" })).toBeVisible();
    view.rerender(viewFor("different operator"));
    expect(screen.queryByText("7789.75")).not.toBeInTheDocument();
    const denial = await screen.findByRole("alert");
    expect(denial).toHaveTextContent("Permission denied");
    expect(denial).toHaveTextContent("Posted ledger amount");
    expect(screen.queryByText("Private posting name")).not.toBeInTheDocument();
  });

  it("does not display complete proof with an omitted component", async () => {
    const proof = createAmountPassport();
    proof.components = proof.components.filter(component => component.component !== "permissions");
    vi.mocked(getAmountPassport).mockResolvedValue(proof);
    render(<AmountPassportTrigger selection={{ label: "Posting", displayValue: "$125.50", reference: proof.reference }} />);
    await userEvent.click(screen.getByRole("button", { name: /Inspect Posting/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Scope mismatch");
    expect(screen.getByRole("dialog")).toHaveTextContent("$125.50");
    expect(screen.queryByText("Complete")).not.toBeInTheDocument();
  });
});
