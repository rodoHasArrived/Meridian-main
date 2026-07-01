import { Plus, Trash2, Edit2, CheckCircle2 } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { EmptyState } from "@/components/data/concrete";

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
  const [isSaving, setIsSaving] = useState(false);
  const dialogTitleId = "symbol-universe-dialog-title";
  const dialogDescriptionId = "symbol-universe-dialog-description";

  const handleOpenNew = () => {
    setEditingSymbol(null);
    setFormData({ exchange: "SMART", currency: "USD", subscribeTrades: true, subscribeDepth: true });
    setIsOpen(true);
  };

  const handleOpenEdit = (symbol: Symbol) => {
    setEditingSymbol(symbol);
    setFormData(symbol);
    setIsOpen(true);
  };

  const handleSave = async () => {
    if (!formData.symbol) return;

    setIsSaving(true);
    try {
      if (editingSymbol) {
        await onUpdate(formData as Symbol);
      } else {
        await onAdd(formData as Symbol);
      }
      setIsOpen(false);
      setFormData({});
      setEditingSymbol(null);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (symbol: string) => {
    if (confirm(`Remove ${symbol} from trading universe?`)) {
      try {
        await onDelete(symbol);
      } catch (error) {
        console.error("Failed to delete symbol:", error);
      }
    }
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
              <Plus className="h-4 w-4 mr-1.5" />
              Add Symbol
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {symbols.length === 0 ? (
            <EmptyState
              icon="table"
              title="No symbols configured yet."
              detail="Add your first symbol to get started."
            />
          ) : (
            <div className="grid gap-2">
              {symbols.map((sym) => (
                <div
                  key={sym.symbol}
                  className="flex items-center justify-between p-3 rounded-[var(--radius-card,2px)] border border-border/70 bg-background/35 transition-colors hover:border-border hover:bg-secondary/40"
                >
                  <div className="flex items-center gap-3 flex-1">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-foreground">{sym.symbol}</span>
                        <Badge variant="outline" className="text-xs">
                          {sym.exchange}
                        </Badge>
                        {sym.status === "configured" && (
                          <CheckCircle2 className="h-4 w-4 text-success" aria-label="Configured" />
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
                      disabled={isLoading}
                      aria-label={`Edit ${sym.symbol}`}
                    >
                      <Edit2 className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDelete(sym.symbol)}
                      disabled={isLoading}
                      aria-label={`Delete ${sym.symbol}`}
                    >
                      <Trash2 className="h-4 w-4 text-danger" />
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
                onChange={(e) => setFormData({ ...formData, symbol: e.target.value.toUpperCase() })}
                disabled={!!editingSymbol || isSaving}
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <Label htmlFor="exchange">Exchange</Label>
                <Input
                  id="exchange"
                  placeholder="e.g., SMART"
                  value={formData.exchange || ""}
                  onChange={(e) => setFormData({ ...formData, exchange: e.target.value })}
                  disabled={isSaving}
                />
              </div>
              <div>
                <Label htmlFor="currency">Currency</Label>
                <Input
                  id="currency"
                  placeholder="e.g., USD"
                  value={formData.currency || ""}
                  onChange={(e) => setFormData({ ...formData, currency: e.target.value })}
                  disabled={isSaving}
                />
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
            <Button onClick={handleSave} disabled={!formData.symbol || isSaving}>
              {isSaving ? "Saving..." : "Save"}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
