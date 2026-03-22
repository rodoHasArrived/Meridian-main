import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

interface WorkspacePlaceholderProps {
  title: string;
  description: string;
}

export function WorkspacePlaceholder({ title, description }: WorkspacePlaceholderProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm leading-6 text-muted-foreground">
        <p>This workspace shell is intentionally present so navigation, routing, and workstation information architecture are locked in early.</p>
        <p>Future slices can now reuse the shared app shell, command palette, data-table pattern, and semantic status tokens.</p>
      </CardContent>
    </Card>
  );
}
