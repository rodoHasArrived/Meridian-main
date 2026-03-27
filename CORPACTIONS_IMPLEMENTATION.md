# Security Master Corporate Actions Tab - Implementation Summary

## Overview
Successfully implemented a **Corporate Actions tab** in the Meridian Security Master WPF detail pane. The tab allows users to:
- View a list of corporate actions (dividends, stock splits) for a security
- Record new corporate actions via a form

## Files Modified

### 1. `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs`

**Added Properties:**
- `ObservableCollection<CorporateActionDto> CorporateActions` — stores the loaded corporate actions
- `IReadOnlyList<string> CorpActTypes` — combo box source, displays ["Dividend", "StockSplit"]
- `int SelectedDetailTab` — tracks the active tab (0=Details, 1=History, 2=Corporate Actions)
- `bool IsRecordCorpActionVisible` — controls visibility of the record form (collapsed by default)
- `string CorpActType` — selected corporate action type (default: "Dividend")
- `string CorpActExDate` — date string input (user-facing, e.g., "2024-01-15")
- `decimal CorpActAmount` — DividendPerShare or SplitRatio value
- `string CorpActCurrency` — currency code (default: "USD")

**Added Commands:**
- `LoadCorporateActionsCommand` — AsyncRelayCommand that calls `GET /api/workstation/security-master/securities/{id}/corporate-actions`
- `ShowRecordCorpActionCommand` — RelayCommand that shows the record form and resets fields
- `CancelRecordCorpActionCommand` — RelayCommand that hides the form and resets fields
- `RecordCorpActionCommand` — AsyncRelayCommand that:
  - Validates ExDate and Amount
  - Parses the date string to DateOnly
  - Builds a CorporateActionDto with nullable fields based on EventType:
    - Dividend: populates DividendPerShare and Currency
    - StockSplit: populates SplitRatio (Currency left null)
  - Posts to `POST /api/workstation/security-master/securities/{id}/corporate-actions`
  - Reloads the CorporateActions collection on success

**Updated Methods:**
- `LoadDetailAsync()` — now calls `LoadCorporateActionsCommand` after loading detail and history

### 2. `src/Meridian.Wpf/Views/SecurityMasterPage.xaml`

**Replaced the detail pane's history section** with a **TabControl** containing three tabs:

**Tab 0: Details**
- Asset Class
- Currency
- Version
- Primary Identifier

**Tab 1: History**
- Event history TextBlock (read-only, Consolas font, 11pt)

**Tab 2: Corporate Actions**
- **Toolbar:** "+ Record" button (enabled only when security is selected)
- **Record Form** (collapsed by default, shows when `IsRecordCorpActionVisible` is true):
  - Type: ComboBox with ["Dividend", "StockSplit"]
  - Ex-Date: TextBox for date input (yyyy-MM-dd format)
  - Amount/Ratio: TextBox for decimal input
  - Currency: TextBox (enabled only for Dividend type)
  - Save & Cancel buttons
- **DataGrid:** Read-only list of corporate actions with columns:
  - Type (EventType)
  - Ex-Date (formatted as yyyy-MM-dd)
  - Amount/Ratio (DataTrigger shows DividendPerShare if Dividend, SplitRatio if StockSplit)
  - Currency

## Architecture & Design Decisions

### ViewModel Patterns
- All properties follow the `SetProperty<T>` pattern from `BindableBase`
- Commands use `CommunityToolkit.Mvvm.Input` (RelayCommand, AsyncRelayCommand)
- Dispatcher marshaling ensures UI updates occur on the UI thread
- Proper error handling with logging and user notifications

### UI Patterns
- TabControl for multi-view detail pane (keeps code-behind thin)
- DataGrid with DataTrigger for conditional column display (Type-aware Amount/Ratio)
- Form visibility toggle for record panel (collapsed by default to save space)
- Static resources for brushes and styles (follows project conventions)

### Data Flow
1. User selects a security in the results list
2. `LoadDetailAsync()` fetches security details, history, and corporate actions in parallel
3. Detail pane tabs populate with the loaded data
4. User can click "Details", "History", or "Corporate Actions" tabs to switch views
5. On "Corporate Actions" tab, user can click "+ Record" to show the form
6. Form validates input and posts the CorporateActionDto to the backend
7. On success, the list reloads and form is cleared/hidden

## Validation & Error Handling

The form validates:
- **ExDate**: Must be parseable as DateOnly in yyyy-MM-dd format
- **Amount/Ratio**: Must be > 0
- **Notifications**: Success/error messages shown via `NotificationService`
- **Logging**: All errors logged via `LoggingService`

## Code Quality

✅ Follows Meridian conventions:
- All async methods have `CancellationToken ct` parameter
- Proper use of `CancellationToken.None` where not needed
- Structured logging with `_logger.LogError()`
- All commands properly wired in constructor
- No code-behind business logic (pure MVVM)
- Static resource usage for brushes (project style)
- Classes marked `sealed` where appropriate

✅ XAML follows project standards:
- Consistent margins and padding
- Uses project-defined styles and brushes
- Proper visibility bindings with converters
- Clean tab organization

## Testing Notes

The implementation:
- Loads corporate actions asynchronously on detail load
- Handles empty corporate action lists gracefully
- Sorts loaded actions by ExDate descending
- Clears the collection when a new security is selected
- Validates form input before posting
- Reloads the list after successful record creation

## Next Steps (if needed)

1. Backend endpoints verification:
   - `GET /api/workstation/security-master/securities/{id}/corporate-actions` should return `CorporateActionDto[]`
   - `POST /api/workstation/security-master/securities/{id}/corporate-actions` should accept and persist `CorporateActionDto`

2. Future enhancements (out of scope):
   - Delete/Edit buttons for existing corporate actions
   - Bulk import from CSV
   - PayDate field support
   - Additional corporate action types (acquisitions, rights offerings, etc.)

