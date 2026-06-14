import { Plus, Trash2, Edit2, CheckCircle2, AlertCircle } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const TICKER_PATTERN = /^[A-Z0-9.\-]{1,12}$/;

interface Symbol {
  symbol: string;
  exchange: string;
  currency: string;
  subscribeTrades: boolean;
  subscribeDepth: boolean;
  status: "configured" | "unconfigured";
  dataFilesCount: number;
  lastDataPoint?: string;
}

interface SymbolUniverseManagerProps {
  symbols: Symbol[];
  onAdd: (symbol: Symbol) => Promise<void>;
  onUpdate: (symbol: Symbol) => Promise<void>;
  onDelete: (symbol: string) => Promise<void>;
  isLoading?: boolean;
}

export function SymbolUniverseManager({ symbols, onAdd, onUpdate, onDelete, isLoading }: SymbolUniverseManagerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [editingSymbol, setEditingSymbol] = useState<Symbol | null>(null);
  const [formData, setFormData] = useState<Partial<Symbol>>({});
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [symbolToDelete, setSymbolToDelete] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const dialogTitleId = "symbol-universe-dialog-title";
  const dialogDescriptionId = "symbol-universe-dialog-description";

  const handleOpenNew = () => {
    setEditingSymbol(null);
    setFormData({ exchange: "SMART", currency: "USD", subscribeTrades: true, subscribeDepth: true });
    setFormErrors({});
    setIsOpen(true);
  };

  const handleOpenEdit = (symbol: Symbol) => {
    setEditingSymbol(symbol);
    setFormData(symbol);
    setFormErrors({});
    setIsOpen(true);
  };

  const validateForm = (): Record<string, string> => {
    const errors: Record<string, string> = {};
    const sym = (formData.symbol ?? "").trim();
    if (!sym) {
      errors.symbol = "Symbol is required.";
    } else if (!TICKER_PATTERN.test(sym)) {
      errors.symbol = "Symbol must be 1–12 uppercase letters, digits, dots, or hyphens.";
    }
    if (!(formData.exchange ?? "").trim()) {
      errors.exchange = "Exchange is required.";
    }
    if (!(formData.currency ?? "").trim()) {
      errors.currency = "Currency is required.";
    }
    return errors;
  };

  const handleSave = async () => {
    const errors = validateForm();
    if (Object.keys(errors).length > 0) {
      setFormErrors(errors);
      return;
    }

    setIsSaving(true);
    try {
      if (editingSymbol) {
        await onUpdate(formData as Symbol);
      } else {
        await onAdd(formData as Symbol);
      }
      setIsOpen(false);
      setFormData({});
      setFormErrors({});
      setEditingSymbol(null);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDeleteRequest = (symbol: string) => {
    setDeleteError(null);
    setSymbolToDelete(symbol);
  };

  const handleDeleteConfirm = async () => {
    if (!symbolToDelete) return;
    setIsDeleting(true);
    try {
      await onDelete(symbolToDelete);
      setSymbolToDelete(null);
    } catch (error) {
      setDeleteError(error instanceof Error ? error.message : "Failed to remove symbol. Please try again.");
    } finally {
      setIsDeleting(false);
    }
  };

  const handleDeleteCancel = () => {
    setSymbolToDelete(null);
    setDeleteError(null);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle>Trading Universe</CardTitle>
              <CardDescription>
                Manage symbols for monitoring, data collection, and trading.
              </CardDescription>
            </div>
            <Button onClick={handleOpenNew} size="sm" disabled={isLoading}>
              <Plus className="h-4 w-4 mr-1.5" aria-hidden="true" />
              Add Symbol
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {deleteError && (
            <div role="alert" className="mb-3 flex items-center gap-2 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
              {deleteError}
            </div>
          )}
          {symbolToDelete && (
            <div role="alertdialog" aria-label={`Confirm removal of ${symbolToDelete}`} className="mb-3 rounded-md border px-3 py-3 text-sm">
              <p className="font-medium">Remove <span className="font-semibold">{symbolToDelete}</span> from the trading universe?</p>
              <div className="mt-2 flex gap-2">
                <Button size="sm" variant="destructive" onClick={handleDeleteConfirm} disabled={isDeleting}>
                  {isDeleting ? "Removing…" : "Remove"}
                </Button>
                <Button size="sm" variant="outline" onClick={handleDeleteCancel} disabled={isDeleting}>
                  Cancel
                </Button>
              </div>
            </div>
          )}
          {symbols.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground mb-2" aria-hidden="true" />
              <p className="text-sm text-muted-foreground">No symbols configured yet.</p>
              <p className="text-xs text-muted-foreground mt-1">Add your first symbol to get started.</p>
            </div>
          ) : (
            <div className="grid gap-2">
              {symbols.map((sym) => (
                <div
                  key={sym.symbol}
                  className="flex items-center justify-between p-3 rounded-lg border hover:bg-muted/50 transition-colors"
                >
                  <div className="flex items-center gap-3 flex-1">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold">{sym.symbol}</span>
                        <Badge variant="outline" className="text-xs">
                          {sym.exchange}
                        </Badge>
                        {sym.status === "configured" && (
                          <CheckCircle2 className="h-4 w-4 text-green-600" aria-hidden="true" />
                        )}
                      </div>
                      <div className="text-xs text-muted-foreground mt-1">
                        {sym.currency} • Trades: {sym.subscribeTrades ? "✓" : "✗"} • Depth: {sym.subscribeDepth ? "✓" : "✗"}
                      </div>
                      {sym.dataFilesCount > 0 && (
                        <div className="text-xs text-muted-foreground mt-1">
                          {sym.dataFilesCount} data files • Last update: {sym.lastDataPoint || "Unknown"}
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleOpenEdit(sym)}
                      disabled={isLoading || symbolToDelete !== null}
                      aria-label={`Edit ${sym.symbol}`}
                    >
                      <Edit2 className="h-4 w-4" aria-hidden="true" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDeleteRequest(sym.symbol)}
                      disabled={isLoading || symbolToDelete !== null}
                      aria-label={`Delete ${sym.symbol}`}
                    >
                      <Trash2 className="h-4 w-4 text-red-500" aria-hidden="true" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={isOpen} onOpenChange={setIsOpen}>
        <DialogContent aria-labelledby={dialogTitleId} aria-describedby={dialogDescriptionId}>
          <DialogHeader>
            <DialogTitle id={dialogTitleId}>{editingSymbol ? "Edit Symbol" : "Add Symbol"}</DialogTitle>
            <DialogDescription id={dialogDescriptionId}>
              Configure a symbol for market data collection and trading.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <div>
              <Label htmlFor="symbol">Symbol *</Label>
              <Input
                id="symbol"
                placeholder="e.g., AAPL"
                value={formData.symbol || ""}
                onChange={(e) => {
                  setFormData({ ...formData, symbol: e.target.value.toUpperCase() });
                  if (formErrors.symbol) setFormErrors({ ...formErrors, symbol: "" });
                }}
                disabled={!!editingSymbol || isSaving}
                aria-required="true"
                aria-describedby={formErrors.symbol ? "symbol-error" : undefined}
                aria-invalid={!!formErrors.symbol}
              />
              {formErrors.symbol && (
                <p id="symbol-error" role="alert" className="mt-1 text-xs text-destructive">{formErrors.symbol}</p>
              )}
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <Label htmlFor="exchange">Exchange *</Label>
                <Input
                  id="exchange"
                  placeholder="e.g., SMART"
                  value={formData.exchange || ""}
                  onChange={(e) => {
                    setFormData({ ...formData, exchange: e.target.value });
                    if (formErrors.exchange) setFormErrors({ ...formErrors, exchange: "" });
                  }}
                  disabled={isSaving}
                  aria-required="true"
                  aria-describedby={formErrors.exchange ? "exchange-error" : undefined}
                  aria-invalid={!!formErrors.exchange}
                />
                {formErrors.exchange && (
                  <p id="exchange-error" role="alert" className="mt-1 text-xs text-destructive">{formErrors.exchange}</p>
                )}
              </div>
              <div>
                <Label htmlFor="currency">Currency *</Label>
                <Input
                  id="currency"
                  placeholder="e.g., USD"
                  value={formData.currency || ""}
                  onChange={(e) => {
                    setFormData({ ...formData, currency: e.target.value });
                    if (formErrors.currency) setFormErrors({ ...formErrors, currency: "" });
                  }}
                  disabled={isSaving}
                  aria-required="true"
                  aria-describedby={formErrors.currency ? "currency-error" : undefined}
                  aria-invalid={!!formErrors.currency}
                />
                {formErrors.currency && (
                  <p id="currency-error" role="alert" className="mt-1 text-xs text-destructive">{formErrors.currency}</p>
                )}
              </div>
            </div>

            <div className="flex gap-4">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.subscribeTrades ?? true}
                  onChange={(e) => setFormData({ ...formData, subscribeTrades: e.target.checked })}
                  disabled={isSaving}
                  className="rounded"
                />
                <span className="text-sm">Subscribe to trades</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.subscribeDepth ?? true}
                  onChange={(e) => setFormData({ ...formData, subscribeDepth: e.target.checked })}
                  disabled={isSaving}
                  className="rounded"
                />
                <span className="text-sm">Subscribe to depth</span>
              </label>
            </div>
          </div>

          <div className="flex gap-3 justify-end pt-4">
            <Button variant="outline" onClick={() => setIsOpen(false)} disabled={isSaving}>
              Cancel
            </Button>
            <Button onClick={handleSave} disabled={isSaving}>
              {isSaving ? "Saving..." : "Save"}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
