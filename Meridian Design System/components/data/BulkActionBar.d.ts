export interface BulkAction {
  id: string;
  label: string;
  title?: string;
  danger?: boolean;
}

export interface BulkActionBarProps {
  selectedCount?: number;
  onAction?: (actionId: string) => void;
  actions?: BulkAction[];
}

export const BulkActionBar: React.FC<BulkActionBarProps>;

export interface BulkSelectCheckboxProps {
  checked: boolean;
  onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  indeterminate?: boolean;
}

export const BulkSelectCheckbox: React.FC<BulkSelectCheckboxProps>;
