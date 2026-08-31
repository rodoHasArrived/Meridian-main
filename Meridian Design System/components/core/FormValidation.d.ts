export interface ValidatorFn {
  (value: any): string | null;
}

export const FormValidation: {
  Validators: {
    required: ValidatorFn;
    email: ValidatorFn;
    number: ValidatorFn;
    minLength: (min: number) => ValidatorFn;
    maxLength: (max: number) => ValidatorFn;
    pattern: (pattern: RegExp, msg: string) => ValidatorFn;
    custom: (fn: ValidatorFn) => ValidatorFn;
  };
  validateField: (value: any, validators: ValidatorFn[]) => string | null;
  validateForm: (state: Record<string, any>, schema: Record<string, ValidatorFn[]>) => Record<string, string>;
};

export interface FieldInputProps {
  label?: string;
  value: any;
  onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onBlur?: (e: React.FocusEvent<HTMLInputElement>) => void;
  validators?: ValidatorFn[];
  hint?: string;
  placeholder?: string;
  type?: string;
}

export const FieldInput: React.FC<FieldInputProps>;

export interface FormErrorSummaryProps {
  errors: Record<string, string | null>;
  fields: Record<string, { label: string }>;
}

export const FormErrorSummary: React.FC<FormErrorSummaryProps>;
