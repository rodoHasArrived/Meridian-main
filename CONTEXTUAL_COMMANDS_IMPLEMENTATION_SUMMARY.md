# CommandPaletteService Contextual Commands Implementation Summary

## Overview
Successfully extended the CommandPaletteService in Meridian WPF to support contextual commands per ViewModel. This allows different pages to register their own set of context-specific commands that appear in the command palette (Ctrl+K) when that page is active.

## Files Created

### 1. **`src/Meridian.Wpf/Services/ICommandContextProvider.cs`** (NEW)
- **Interface** for ViewModels that want to provide contextual commands
- **Methods:**
  - `string ContextKey { get; }` — unique identifier for the context
  - `IReadOnlyList<CommandEntry> GetContextualCommands()` — returns list of commands for this context
  - `void OnActivated()` — called when page becomes active (registers provider with palette service)
  - `void OnDeactivated()` — called when page is navigated away (unregisters provider)

## Files Modified

### 2. **`src/Meridian.Ui.Services/Services/CommandPaletteService.cs`** (EXTENDED)

#### Added Imports
```csharp
using System.Collections.Concurrent;
using System.Windows.Input;
```

#### New Fields
- `_contextualProviders`: `ConcurrentDictionary<string, Func<IReadOnlyList<CommandEntry>>>` — thread-safe registry of contextual command providers
- `_activeContextKey`: `string?` — tracks which context is currently active

#### New Event
- `CommandsChanged` — fired when contextual providers are registered/unregistered or context switches

#### New Public Methods
- `void RegisterContextualProvider(string contextKey, Func<IReadOnlyList<CommandEntry>> provider)` — register a context provider
- `void UnregisterContextualProvider(string contextKey)` — remove a context provider
- `void SetActiveContext(string contextKey)` — activate a specific context
- `void ClearActiveContext()` — deactivate contextual commands

#### New Type: CommandEntry Record
```csharp
public sealed record CommandEntry(
    string Title,
    string? Description,
    string? Category,
    ICommand Command,
    string? Shortcut = null);
```
- Lightweight record for contextual command metadata
- `Title`: Display name of the command
- `Description`: Detailed explanation of what the command does
- `Category`: Logical grouping (e.g., "Backfill", "Symbols", "Dashboard")
- `Command`: ICommand instance to execute
- `Shortcut`: Optional keyboard shortcut display (e.g., "Ctrl+B", "F5")

---

### 3. **`src/Meridian.Wpf/ViewModels/BackfillViewModel.cs`** (EXTENDED)

#### Class Declaration
```csharp
public sealed class BackfillViewModel : BindableBase, IDisposable, ICommandContextProvider
```

#### New Imports
```csharp
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
```

#### ICommandContextProvider Implementation
- **ContextKey**: `"Backfill"`
- **GetContextualCommands()** returns:
  - "Start Backfill" — begin new backfill operation
  - "Pause Backfill" / "Resume Backfill" — toggle pause state
  - "Cancel Backfill" — stop active backfill (if running)
  - "View Backfill Status" — show current status notification
  - "View Backfill Schedule" — navigate to schedules page
- **OnActivated()** — registers provider and sets active context
- **OnDeactivated()** — clears context and unregisters provider

---

### 4. **`src/Meridian.Wpf/ViewModels/SymbolsPageViewModel.cs`** (EXTENDED)

#### Class Declaration
```csharp
public sealed class SymbolsPageViewModel : BindableBase, IDisposable, ICommandContextProvider
```

#### New Imports
```csharp
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
```

#### ICommandContextProvider Implementation
- **ContextKey**: `"Symbols"`
- **GetContextualCommands()** returns:
  - "Add Symbol" — add new symbol to subscription list (Ctrl+N)
  - "Remove Selected" — delete selected symbol from list (Delete)
  - "View in Live Data" — open selected symbol in Live Data viewer
  - "Export Symbols" — navigate to export page
  - "Reload Symbols" — refresh list from configuration (F5)
- **OnActivated()** — registers provider and sets active context
- **OnDeactivated()** — clears context and unregisters provider

---

### 5. **`src/Meridian.Wpf/ViewModels/DashboardViewModel.cs`** (EXTENDED)

#### Class Declaration
```csharp
public sealed class DashboardViewModel : BindableBase, IDisposable, IPageActionBarProvider, ICommandContextProvider
```

#### New Imports
```csharp
using System.Collections.Generic;
using System.Windows.Input;
```

#### ICommandContextProvider Implementation
- **ContextKey**: `"Dashboard"`
- **GetContextualCommands()** returns:
  - "Refresh Dashboard" — refresh all metrics and status (F5)
  - "Start Data Collector" / "Stop Data Collector" — toggle collector state
  - "View Provider Health" — navigate to provider health page
  - "View Data Quality" — navigate to data quality page
  - "View Activity Log" — show recent events
  - "Run Backfill" — start backfill operation
- **OnActivated()** — registers provider and sets active context
- **OnDeactivated()** — clears context and unregisters provider

---

### 6. **`src/Meridian.Wpf/Views/CommandPaletteWindow.xaml`** (UPDATED)

#### Added Namespace
```xml
xmlns:local="clr-namespace:Meridian.Wpf.Converters"
```

#### Enhanced ItemTemplate
```xml
<ListBox.ItemTemplate>
    <DataTemplate>
        <Grid Margin="4">
            <!-- Icon (left) -->
            <TextBlock Grid.Column="0"
                       Text="{Binding Icon}"
                       FontFamily="Segoe MDL2 Assets"
                       FontSize="14"
                       Foreground="#FF89B4FA" />

            <!-- Title & Description (center, expandable) -->
            <StackPanel Grid.Column="1">
                <TextBlock Text="{Binding Title}"
                           FontSize="13"
                           FontWeight="SemiBold"
                           Foreground="#FFCDD6F4" />
                <TextBlock Text="{Binding Description}"
                           FontSize="11"
                           Foreground="#FF6C7086"
                           TextWrapping="Wrap" />
            </StackPanel>

            <!-- Category & Shortcut (right) -->
            <StackPanel Grid.Column="2"
                        Orientation="Vertical"
                        VerticalAlignment="Top">
                <TextBlock Text="{Binding Category}"
                           FontSize="10"
                           Foreground="#FF585B70" />
                
                <Border Background="#FF313244"
                        CornerRadius="3"
                        Padding="4,2">
                    <TextBlock Text="{Binding Shortcut}"
                               FontFamily="Consolas"
                               FontSize="10"
                               Foreground="#FF89B4FA" />
                </Border>
            </StackPanel>
        </Grid>
    </DataTemplate>
</ListBox.ItemTemplate>
```

#### New Window Resources
```xml
<local:NullToCollapsedConverter x:Key="NullToCollapsedConverter" />
```

---

## Key Design Decisions

### 1. **Thread Safety**
- Used `ConcurrentDictionary<T, U>` for `_contextualProviders` to safely register/unregister providers from any thread
- No manual locking required for dictionary operations

### 2. **Lazy Command Resolution**
- Store `Func<IReadOnlyList<CommandEntry>>` instead of `IReadOnlyList<CommandEntry>` directly
- Allows ViewModels to generate commands on-demand based on current state
- Enables dynamic enable/disable of commands without re-registering

### 3. **Lifecycle Pattern**
- `OnActivated()` called when page becomes visible
- `OnDeactivated()` called when page is navigated away
- Clean separation of concerns: palette service doesn't know about navigation

### 4. **Isolated Contexts**
- Only one context active at a time via `_activeContextKey`
- `ClearActiveContext()` disables all contextual commands when no page is active
- Prevents stale commands from unavailable pages appearing in search results

### 5. **UI Enhancements**
- Added Category field to CommandEntry for visual grouping (e.g., "Backfill", "Symbols")
- Added Shortcut field for keyboard shortcut hints (e.g., "Ctrl+B", "F5")
- Improved data template shows title, description, category, and shortcut in organized layout

---

## Integration Points

### For Page Views
When implementing a new page that should provide contextual commands:

1. **Implement ICommandContextProvider on ViewModel**:
   ```csharp
   public string ContextKey => "MyPage";
   
   public IReadOnlyList<CommandEntry> GetContextualCommands()
   {
       var commands = new List<CommandEntry>();
       commands.Add(new CommandEntry(
           "My Command",
           "Description of what this does",
           "Category",
           new RelayCommand(() => MyMethod()),
           "Shortcut"));
       return commands;
   }
   
   public void OnActivated()
   {
       CommandPaletteService.Instance.RegisterContextualProvider(ContextKey, GetContextualCommands);
       CommandPaletteService.Instance.SetActiveContext(ContextKey);
   }
   
   public void OnDeactivated()
   {
       CommandPaletteService.Instance.ClearActiveContext();
       CommandPaletteService.Instance.UnregisterContextualProvider(ContextKey);
   }
   ```

2. **Call OnActivated() in Page.StartAsync()**
3. **Call OnDeactivated() in Page.Stop()**

### For Command Palette Window
- Automatically picks up contextual commands when searching
- No changes needed to CommandPaletteWindow; it observes registered providers

---

## Testing Notes

The implementation follows these patterns:
- ✅ No business logic in code-behind (all in ViewModels)
- ✅ Thread-safe dictionary access
- ✅ Follows MVVM with BindableBase
- ✅ Uses RelayCommand and AsyncRelayCommand from MVVM Toolkit
- ✅ Sealed classes where applicable
- ✅ No Version="" on PackageReferences
- ✅ CancellationToken support where appropriate

---

## File Summary

| File | Type | Status |
|------|------|--------|
| `ICommandContextProvider.cs` | NEW | Interface definition |
| `CommandPaletteService.cs` | MODIFIED | Added contextual provider system, CommandEntry record |
| `BackfillViewModel.cs` | MODIFIED | Implemented ICommandContextProvider |
| `SymbolsPageViewModel.cs` | MODIFIED | Implemented ICommandContextProvider |
| `DashboardViewModel.cs` | MODIFIED | Implemented ICommandContextProvider |
| `CommandPaletteWindow.xaml` | MODIFIED | Enhanced data template, added converter |

---

## Runtime Behavior

1. **User navigates to Dashboard page**
   - DashboardViewModel.OnActivated() is called
   - Dashboard's context provider is registered
   - Dashboard context becomes active
   - Ctrl+K now shows Dashboard-specific commands

2. **User searches in command palette**
   - Search includes both global commands and dashboard contextual commands
   - Results show category and shortcut hints

3. **User navigates to Backfill page**
   - DashboardViewModel.OnDeactivated() clears dashboard context
   - BackfillViewModel.OnActivated() registers backfill context
   - Search results now show backfill-specific commands

4. **User executes contextual command**
   - Command executes via the bound ICommand in CommandEntry
   - CommandPaletteService.Execute() tracks recent usage as normal

---

## No Breaking Changes
- Existing global commands continue to work unchanged
- Existing Search() behavior unchanged for non-contextual scenarios
- Execute() behavior unchanged
- All new members are additions only
