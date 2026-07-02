import { StatementImportPanel } from "@/screens/statement-import-panel";

/**
 * Thin route wrapper so the Accounting workspace lazy-route pattern in `app.tsx` stays
 * consistent (`*-screen` component per route). All behavior lives in `StatementImportPanel`.
 */
export function StatementImportScreen() {
  return <StatementImportPanel />;
}
