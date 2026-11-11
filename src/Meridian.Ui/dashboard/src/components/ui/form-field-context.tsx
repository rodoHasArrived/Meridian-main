import { createContext, useContext, type AriaAttributes } from "react";

export interface FormFieldContextValue {
  controlId: string;
  descriptionId?: string;
  invalid: boolean;
}

export const FormFieldContext = createContext<FormFieldContextValue | null>(null);

interface FieldAccessibilityProps {
  id?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: AriaAttributes["aria-invalid"];
}

/** Shared controls consume FormRow assistance. Custom fields can use the same hook. */
export function useFormFieldProps(props: FieldAccessibilityProps = {}, invalid = false): FieldAccessibilityProps {
  const field = useContext(FormFieldContext);
  // A row may contain secondary controls. Only its labelled field inherits its help/error.
  const belongsToField = field && (!props.id || props.id === field.controlId);
  const descriptionIds = [props["aria-describedby"], belongsToField ? field.descriptionId : undefined]
    .filter(Boolean).join(" ").split(/\s+/).filter(Boolean);
  return {
    id: props.id ?? field?.controlId,
    "aria-describedby": descriptionIds.length ? [...new Set(descriptionIds)].join(" ") : undefined,
    "aria-invalid": props["aria-invalid"] ?? (invalid || (belongsToField && field.invalid) ? true : undefined)
  };
}
