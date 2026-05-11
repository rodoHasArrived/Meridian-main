import { CheckCircle2, AlertCircle, Eye, EyeOff, Loader2, ExternalLink } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

interface ProviderField {
  name: string;
  label: string;
  required: boolean;
  type: "text" | "password" | "email" | "number";
  help?: string;
}

interface ProviderRequirement {
  name: string;
  description: string;
  type: "trading_broker" | "data_provider";
  supportsDemo: boolean;
  fields: ProviderField[];
  docs: string;
  supportedDataTypes: string[];
}

interface ProviderCredentialSetupProps {
  providers: Record<string, ProviderRequirement>;
  onSave: (provider: string, credentials: Record<string, string>) => Promise<void>;
  onTest: (provider: string, credentials: Record<string, string>) => Promise<boolean>;
  isLoading?: boolean;
}

export function ProviderCredentialSetup({ providers, onSave, onTest, isLoading }: ProviderCredentialSetupProps) {
  const [selectedProvider, setSelectedProvider] = useState<string | null>(null);
  const [credentials, setCredentials] = useState<Record<string, string>>({});
  const [showPasswords, setShowPasswords] = useState<Record<string, boolean>>({});
  const [isTesting, setIsTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const provider = selectedProvider ? providers[selectedProvider] : null;

  const handleSelectProvider = (providerName: string) => {
    setSelectedProvider(providerName);
    setCredentials({});
    setTestResult(null);
    setShowPasswords({});
  };

  const handleTest = async () => {
    if (!selectedProvider) return;

    setIsTesting(true);
    try {
      const success = await onTest(selectedProvider, credentials);
      setTestResult({
        success,
        message: success ? "Connection successful!" : "Connection failed. Please check your credentials.",
      });
    } catch (error) {
      setTestResult({
        success: false,
        message: `Test failed: ${error instanceof Error ? error.message : "Unknown error"}`,
      });
    } finally {
      setIsTesting(false);
    }
  };

  const handleSave = async () => {
    if (!selectedProvider) return;

    setIsSaving(true);
    try {
      await onSave(selectedProvider, credentials);
      setSelectedProvider(null);
      setCredentials({});
      setTestResult(null);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Card>
      <CardHeader>
        <div>
          <CardTitle>Data Provider Credentials</CardTitle>
          <CardDescription>
            Configure API keys and connection details for market data providers.
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent>
        <div className="grid gap-4">
          {/* Provider list */}
          <div className="space-y-2">
            <Label>Available Providers</Label>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              {Object.entries(providers).map(([key, prov]) => (
                <button
                  key={key}
                  onClick={() => handleSelectProvider(key)}
                  className={cn(
                    "p-3 rounded-lg border text-left transition-colors hover:bg-muted",
                    selectedProvider === key && "border-primary bg-primary/5",
                  )}
                  disabled={isLoading}
                >
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="font-semibold text-sm">{prov.name}</div>
                      <div className="text-xs text-muted-foreground">{prov.description}</div>
                    </div>
                    <Badge variant={prov.supportsDemo ? "success" : "outline"} className="text-xs">
                      {prov.supportsDemo ? "Demo" : "Live"}
                    </Badge>
                  </div>
                </button>
              ))}
            </div>
          </div>

          {/* Credential form */}
          {provider && selectedProvider && (
            <Dialog open={!!selectedProvider} onOpenChange={() => setSelectedProvider(null)}>
              <DialogContent className="max-w-md">
                <DialogHeader>
                  <DialogTitle>{provider.name} Credentials</DialogTitle>
                  <DialogDescription>{provider.description}</DialogDescription>
                </DialogHeader>

                <div className="space-y-4 py-4">
                  {/* Data types supported */}
                  <div>
                    <Label className="text-xs font-semibold text-muted-foreground">Supported Data Types</Label>
                    <div className="flex flex-wrap gap-1 mt-2">
                      {provider.supportedDataTypes.map((type) => (
                        <Badge key={type} variant="outline" className="text-xs">
                          {type}
                        </Badge>
                      ))}
                    </div>
                  </div>

                  {/* Credential fields */}
                  {provider.fields.length > 0 ? (
                    <div className="space-y-3">
                      {provider.fields.map((field) => (
                        <div key={field.name}>
                          <Label htmlFor={field.name} className="text-sm">
                            {field.label} {field.required && <span className="text-red-500">*</span>}
                          </Label>
                          <div className="relative mt-1.5">
                            <Input
                              id={field.name}
                              type={field.type === "password" && !showPasswords[field.name] ? "password" : "text"}
                              placeholder={field.help || "Enter value"}
                              value={credentials[field.name] || ""}
                              onChange={(e) =>
                                setCredentials({ ...credentials, [field.name]: e.target.value })
                              }
                              disabled={isTesting || isSaving}
                              className="pr-10"
                            />
                            {field.type === "password" && (
                              <button
                                type="button"
                                onClick={() =>
                                  setShowPasswords({
                                    ...showPasswords,
                                    [field.name]: !showPasswords[field.name],
                                  })
                                }
                                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                              >
                                {showPasswords[field.name] ? (
                                  <EyeOff className="h-4 w-4" />
                                ) : (
                                  <Eye className="h-4 w-4" />
                                )}
                              </button>
                            )}
                          </div>
                          {field.help && <p className="text-xs text-muted-foreground mt-1">{field.help}</p>}
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="p-3 rounded-lg bg-green-50 border border-green-200">
                      <p className="text-sm text-green-900">
                        ✓ {provider.name} doesn't require credentials.
                      </p>
                    </div>
                  )}

                  {/* Test result */}
                  {testResult && (
                    <div
                      className={cn(
                        "p-3 rounded-lg border flex gap-2",
                        testResult.success
                          ? "bg-green-50 border-green-200"
                          : "bg-red-50 border-red-200",
                      )}
                    >
                      {testResult.success ? (
                        <CheckCircle2 className="h-4 w-4 text-green-600 flex-shrink-0 mt-0.5" />
                      ) : (
                        <AlertCircle className="h-4 w-4 text-red-600 flex-shrink-0 mt-0.5" />
                      )}
                      <p
                        className={cn(
                          "text-sm",
                          testResult.success ? "text-green-900" : "text-red-900",
                        )}
                      >
                        {testResult.message}
                      </p>
                    </div>
                  )}
                </div>

                {/* Actions */}
                <div className="flex gap-3 justify-end pt-4 border-t">
                  <Button
                    variant="outline"
                    onClick={handleTest}
                    disabled={!credentials || isTesting || isSaving}
                    size="sm"
                  >
                    {isTesting && <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />}
                    Test Connection
                  </Button>

                  <Button
                    onClick={handleSave}
                    disabled={!credentials || isSaving}
                    size="sm"
                  >
                    {isSaving && <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />}
                    Save Credentials
                  </Button>
                </div>

                {/* Documentation link */}
                {provider.docs && (
                  <div className="pt-2 border-t">
                    <a
                      href={provider.docs}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-xs text-primary hover:underline flex items-center gap-1"
                    >
                      View documentation
                      <ExternalLink className="h-3 w-3" />
                    </a>
                  </div>
                )}
              </DialogContent>
            </Dialog>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
