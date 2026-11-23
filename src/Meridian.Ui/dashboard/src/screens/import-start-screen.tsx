import { Link, useLocation } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export function ImportStartScreen() {
  const location = useLocation();
  return <section className="workspace-section-band" aria-labelledby="import-start-heading">
    <h1 id="import-start-heading" className="text-xl font-semibold">Import</h1>
    <p>Choose what you are bringing into Meridian. Review the mapping, scope, and retained evidence before committing.</p>
    <div className="grid gap-4 lg:grid-cols-2">
      <div className="rounded border border-border p-4"><h2 className="font-semibold">Onboarding workbook</h2><p className="my-3 text-sm">Load supported organization and account setup records through the governed workbook flow.</p><Button asChild variant="outline"><Link to={`${WORKSTATION_ROUTE_CATALOG.dataImport}${location.search}`}>Import workbook</Link></Button></div>
      <div className="rounded border border-border p-4"><h2 className="font-semibold">Bank or custodian statement</h2><p className="my-3 text-sm">Preview a statement file or configure a supported scheduled fetch, then review reconciliation outcomes.</p><Button asChild variant="outline"><Link to={`${WORKSTATION_ROUTE_CATALOG.accountingStatementImport}${location.search}`}>Import statement</Link></Button></div>
    </div>
  </section>;
}
