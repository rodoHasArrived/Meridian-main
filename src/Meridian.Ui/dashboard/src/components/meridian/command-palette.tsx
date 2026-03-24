import { ArrowRight, LayoutPanelTop, Sparkles } from "lucide-react";
import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator
} from "@/components/ui/command";
import { WORKSPACE_COMMANDS, WORKSPACES, workspacePath } from "@/lib/workspace";

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const navigate = useNavigate();

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        onOpenChange(!open);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onOpenChange, open]);

  const goTo = (href: string) => {
    navigate(href);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="left-1/2 top-[20%] right-auto inset-y-auto w-[min(680px,calc(100vw-2rem))] -translate-x-1/2 p-0">
        <DialogHeader className="sr-only">
          <DialogTitle>Meridian command palette</DialogTitle>
          <DialogDescription>Search workspaces and quick workstation actions.</DialogDescription>
        </DialogHeader>
        <Command label="Meridian command palette">
          <CommandInput placeholder="Search workspaces, workflows, and quick actions..." />
          <CommandList>
            <CommandEmpty>No command matches your query.</CommandEmpty>
            <CommandGroup heading="Workspaces">
              {WORKSPACES.map((workspace) => (
                <CommandItem key={workspace.key} onSelect={() => goTo(workspacePath(workspace.key))}>
                  <LayoutPanelTop className="h-4 w-4 text-primary" />
                  <span className="flex-1">
                    <span className="block font-medium">{workspace.label}</span>
                    <span className="block text-xs text-muted-foreground">{workspace.description}</span>
                  </span>
                  <ArrowRight className="h-4 w-4 text-muted-foreground" />
                </CommandItem>
              ))}
            </CommandGroup>
            <CommandSeparator />
            {WORKSPACES.map((workspace, index) => (
              <div key={`${workspace.key}-commands`}>
                <CommandGroup heading={`${workspace.label} Actions`}>
                  {WORKSPACE_COMMANDS
                    .filter((command) => command.workspace === workspace.key)
                    .map((command) => (
                      <CommandItem key={command.id} onSelect={() => goTo(command.href)}>
                        <Sparkles className="h-4 w-4 text-primary" />
                        <span className="flex-1">
                          <span className="block font-medium">{command.label}</span>
                          <span className="block text-xs text-muted-foreground">{command.description}</span>
                        </span>
                        <ArrowRight className="h-4 w-4 text-muted-foreground" />
                      </CommandItem>
                    ))}
                </CommandGroup>
                {index < WORKSPACES.length - 1 ? <CommandSeparator /> : null}
              </div>
            ))}
          </CommandList>
        </Command>
      </DialogContent>
    </Dialog>
  );
}
