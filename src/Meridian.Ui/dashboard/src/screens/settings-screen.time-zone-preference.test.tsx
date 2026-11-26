import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { getDisplayTimeZone, setDisplayTimeZone } from "@/lib/format";
import { DISPLAY_TIME_ZONE_STORAGE_KEY } from "@/lib/display-time-zone";
import { SettingsTimeZonePreference } from "@/screens/settings-screen.time-zone-preference";

afterEach(() => {
  setDisplayTimeZone("UTC");
});

describe("SettingsTimeZonePreference", () => {
  it("defaults to UTC and switches to a common zone", () => {
    render(<SettingsTimeZonePreference />);

    const select = screen.getByLabelText("Time zone");
    expect(select).toHaveValue("UTC");
    expect(screen.getByText(/e\.g\. Jun 30, 2026, 18:05 UTC/)).toBeInTheDocument();

    fireEvent.change(select, { target: { value: "America/New_York" } });
    expect(getDisplayTimeZone()).toBe("America/New_York");
    expect(localStorage.getItem(DISPLAY_TIME_ZONE_STORAGE_KEY)).toBe("America/New_York");
    expect(screen.getByText(/e\.g\. Jun 30, 2026, 14:05 EDT/)).toBeInTheDocument();
  });

  it("accepts a valid IANA zone typed in and rejects an unknown one", () => {
    render(<SettingsTimeZonePreference />);
    const input = screen.getByLabelText("Other IANA time zone");

    fireEvent.change(input, { target: { value: "Mars/Olympus_Mons" } });
    fireEvent.click(screen.getByRole("button", { name: "Use zone" }));
    expect(screen.getByRole("alert")).toHaveTextContent('"Mars/Olympus_Mons" is not an IANA time zone');
    expect(getDisplayTimeZone()).toBe("UTC");

    fireEvent.change(input, { target: { value: "Europe/Madrid" } });
    fireEvent.click(screen.getByRole("button", { name: "Use zone" }));
    expect(screen.queryByRole("alert")).toBeNull();
    expect(getDisplayTimeZone()).toBe("Europe/Madrid");
    expect(screen.getByLabelText("Time zone")).toHaveValue("Europe/Madrid");
  });
});
