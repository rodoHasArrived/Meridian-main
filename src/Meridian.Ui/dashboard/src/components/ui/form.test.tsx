import { render, screen } from "@testing-library/react";
import { FormRow, useFormFieldProps } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TextArea } from "@/components/ui/text-area";
import { NumberInput } from "@/components/ui/number-input";

describe("FormRow assistance", () => {
  it("associates generated labels and assistance and preserves caller descriptions", () => {
    const { rerender } = render(<>
      <p id="unit-help">USD per unit.</p>
      <FormRow label="Price" labelFor="price" hint="Use a retained observation.">
        <Input id="price" aria-describedby="unit-help" />
      </FormRow>
    </>);
    const price = screen.getByRole("textbox", { name: "Price" });
    expect(price).toHaveAccessibleDescription("USD per unit. Use a retained observation.");
    expect(price).not.toHaveAttribute("aria-invalid");
    rerender(<FormRow label="Price" labelFor="price" hint="Use a retained observation." error="Price is required."><Input id="price" /></FormRow>);
    const invalidPrice = screen.getByRole("textbox", { name: "Price" });
    expect(invalidPrice).toHaveAttribute("aria-invalid", "true");
    expect(invalidPrice).toHaveAccessibleDescription("Price is required.");
  });

  it("supports controls without explicit IDs and keeps separate fields isolated", () => {
    render(<>
      <FormRow label="Book" hint="Choose the account-owned book."><Select><option>Primary</option></Select></FormRow>
      <FormRow label="Rationale" error="Give a decision reason."><TextArea /></FormRow>
      <FormRow label="Quantity" hint="Number of units."><NumberInput /></FormRow>
    </>);
    const book = screen.getByRole("combobox", { name: "Book" });
    const rationale = screen.getByRole("textbox", { name: "Rationale" });
    const quantity = screen.getByRole("spinbutton", { name: "Quantity" });
    expect(book).toHaveAccessibleDescription("Choose the account-owned book.");
    expect(rationale).toHaveAccessibleDescription("Give a decision reason.");
    expect(rationale).toHaveAttribute("aria-invalid", "true");
    expect(quantity).toHaveAccessibleDescription("Number of units.");
    expect(new Set([book.id, rationale.id, quantity.id]).size).toBe(3);
  });

  it("offers the same association to custom native fields without annotating secondary controls", () => {
    function NativeField() {
      return <input {...useFormFieldProps({ "aria-describedby": "extra-help extra-help" })} />;
    }
    render(<>
      <p id="extra-help">Enter the code.</p>
      <FormRow label="Account" labelFor="account-code" hint="Use the exact account.">
        <NativeField />
        <Input id="secondary" aria-label="Search accounts" />
      </FormRow>
    </>);
    expect(screen.getByRole("textbox", { name: "Account" })).toHaveAccessibleDescription("Enter the code. Use the exact account.");
    expect(screen.getByRole("textbox", { name: "Search accounts" })).not.toHaveAttribute("aria-describedby");
  });
});
