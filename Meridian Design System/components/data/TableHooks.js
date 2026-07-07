// Meridian TableHooks — capitalized carrier so the table logic hooks reach consumers.
// The compiler only surfaces capital-initial exports on window.<Namespace>; React hooks must
// stay lowercase (`useX`), so they'd otherwise be bundle-internal. Consume as:
//
//   const { useTableState, useTableColumns, useRowSelection, useAsyncTableData } =
//     window.MeridianDesignSystem_4f61be.TableHooks;
import { useTableState } from "./useTableState.js";
import { useTableColumns } from "./useTableColumns.js";
import { useRowSelection } from "./useRowSelection.js";
import { useAsyncTableData } from "./useAsyncTableData.js";

export const TableHooks = { useTableState, useTableColumns, useRowSelection, useAsyncTableData };
