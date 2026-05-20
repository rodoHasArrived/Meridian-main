import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";

describe("field-support", () => {
  it("joins described-by ids while skipping empty values", () => {
    expect(joinDescribedByIds("field-help", null, undefined, false, "field-disabled")).toBe("field-help field-disabled");
    expect(joinDescribedByIds(null, undefined, false)).toBeUndefined();
  });

  it("renders help text and a visible disabled reason", () => {
    render(
      <FieldSupportText
        helpId="field-help"
        helpText="Use whole-dollar paper capital."
        disabledReason="Paper session creation is in progress."
        disabledReasonId="field-disabled-reason"
      />
    );

    expect(screen.getByText("Use whole-dollar paper capital.")).toHaveAttribute("id", "field-help");
    expect(screen.getByText("Paper session creation is in progress.")).toHaveAttribute("id", "field-disabled-reason");
  });
});
