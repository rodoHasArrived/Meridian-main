using System.Globalization;
using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SettingsViewModel
{
    private bool _isOperationsControlBusy;
    private string _operationsControlStatusText = "Operations control center not loaded.";
    private string _operationsApprovalPolicySummaryText = "Approval policy matrix not loaded.";
    private string _operationsCloseCalendarSummaryText = "Close calendar not loaded.";
    private string _operationsControlGeneratedAtText = "-";

    public bool IsOperationsControlBusy
    {
        get => _isOperationsControlBusy;
        private set
        {
            if (SetProperty(ref _isOperationsControlBusy, value))
            {
                UpdateOperationsControlCommandStates();
            }
        }
    }

    public string OperationsControlStatusText
    {
        get => _operationsControlStatusText;
        private set => SetProperty(ref _operationsControlStatusText, value);
    }

    public string OperationsApprovalPolicySummaryText
    {
        get => _operationsApprovalPolicySummaryText;
        private set => SetProperty(ref _operationsApprovalPolicySummaryText, value);
    }

    public string OperationsCloseCalendarSummaryText
    {
        get => _operationsCloseCalendarSummaryText;
        private set => SetProperty(ref _operationsCloseCalendarSummaryText, value);
    }

    public string OperationsControlGeneratedAtText
    {
        get => _operationsControlGeneratedAtText;
        private set => SetProperty(ref _operationsControlGeneratedAtText, value);
    }

    public int OperationsApprovalPolicyRuleCount => OperationsApprovalPolicyRows.Count;

    public int OperationsCloseCalendarItemCount => OperationsCloseCalendarRows.Count;

    public int OperationsCloseCalendarBlockedCount => OperationsCloseCalendarRows.Count(static row => row.BlockerCount > 0);

    public int OperationsCloseCalendarReadyCount => OperationsCloseCalendarRows.Count(static row => row.IsReadyToClose);

    public bool HasOperationsApprovalPolicyRows => OperationsApprovalPolicyRows.Count > 0;

    public bool HasOperationsCloseCalendarRows => OperationsCloseCalendarRows.Count > 0;

    private async Task RefreshOperationsControlAsync()
    {
        if (_operationsControlClient is null)
        {
            SetOperationsControlUnavailableState();
            return;
        }

        IsOperationsControlBusy = true;
        try
        {
            var policy = await _operationsControlClient.GetApprovalPolicyMatrixAsync().ConfigureAwait(true);
            var calendar = await _operationsControlClient.GetCloseCalendarAsync().ConfigureAwait(true);

            ReplaceOperationsApprovalPolicyRows(policy);
            ReplaceOperationsCloseCalendarRows(calendar);

            var generatedAt = new[]
                {
                    policy?.GeneratedAtUtc,
                    calendar?.GeneratedAtUtc
                }
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();

            OperationsControlGeneratedAtText = generatedAt == DateTimeOffset.MinValue
                ? "-"
                : generatedAt.UtcDateTime.ToString("MMM d, HH:mm 'UTC'", CultureInfo.InvariantCulture);
            OperationsControlStatusText = $"Loaded {OperationsApprovalPolicyRuleCount} approval rule(s) and {OperationsCloseCalendarItemCount} close item(s).";
        }
        catch (OperationCanceledException)
        {
            OperationsControlStatusText = "Operations control refresh cancelled.";
        }
        catch (Exception ex)
        {
            OperationsControlStatusText = $"Operations control refresh failed: {ex.Message}";
            _notificationService.ShowNotification("Operations Control Refresh Failed", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsOperationsControlBusy = false;
        }
    }

    private void SetOperationsControlUnavailableState()
    {
        OperationsControlStatusText = "Operations control center is unavailable in this desktop session.";
        OperationsApprovalPolicySummaryText = "Approval policy matrix requires the shared Operations Continuity client.";
        OperationsCloseCalendarSummaryText = "Close calendar requires the shared Operations Continuity client.";
        OperationsControlGeneratedAtText = "-";
        OperationsApprovalPolicyRows.Clear();
        OperationsCloseCalendarRows.Clear();
        UpdateOperationsControlDerivedProperties();
    }

    private void ReplaceOperationsApprovalPolicyRows(OperationsApprovalPolicyMatrixDto? policy)
    {
        OperationsApprovalPolicyRows.Clear();
        if (policy?.Rows is not null)
        {
            foreach (var row in policy.Rows
                         .OrderBy(static item => item.WorkflowArea, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.Gate)
                         .ThenBy(static item => item.Action, StringComparer.OrdinalIgnoreCase))
            {
                OperationsApprovalPolicyRows.Add(new SettingsOperationsApprovalPolicyRow(row));
            }
        }

        OperationsApprovalPolicySummaryText = policy is null
            ? "Approval policy matrix is unavailable."
            : $"{policy.PolicyId} {policy.Version}: {OperationsApprovalPolicyRows.Count} governed approval rule(s).";
        UpdateOperationsControlDerivedProperties();
    }

    private void ReplaceOperationsCloseCalendarRows(OperationsCloseCalendarDto? calendar)
    {
        OperationsCloseCalendarRows.Clear();
        if (calendar?.Items is not null)
        {
            foreach (var item in calendar.Items
                         .OrderBy(static item => item.NextDueDate ?? DateOnly.MaxValue)
                         .ThenBy(static item => item.PeriodId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static item => item.FundAccountId))
            {
                OperationsCloseCalendarRows.Add(new SettingsOperationsCloseCalendarRow(item));
            }
        }

        OperationsCloseCalendarSummaryText = calendar is null
            ? "Close calendar is unavailable."
            : $"{OperationsCloseCalendarItemCount} close item(s), {OperationsCloseCalendarReadyCount} ready, {OperationsCloseCalendarBlockedCount} blocked.";
        UpdateOperationsControlDerivedProperties();
    }

    private bool CanUseOperationsControl() => _operationsControlClient is not null && !IsOperationsControlBusy;

    private void UpdateOperationsControlCommandStates()
    {
        RefreshOperationsControlCommand.NotifyCanExecuteChanged();
    }

    private void UpdateOperationsControlDerivedProperties()
    {
        RaisePropertyChanged(nameof(OperationsApprovalPolicyRuleCount));
        RaisePropertyChanged(nameof(OperationsCloseCalendarItemCount));
        RaisePropertyChanged(nameof(OperationsCloseCalendarBlockedCount));
        RaisePropertyChanged(nameof(OperationsCloseCalendarReadyCount));
        RaisePropertyChanged(nameof(HasOperationsApprovalPolicyRows));
        RaisePropertyChanged(nameof(HasOperationsCloseCalendarRows));
        UpdateOperationsControlCommandStates();
    }

    private static string FormatEnumLabel<T>(T value) where T : struct, Enum
        => FormatIdentifier(value.ToString());

    internal static string FormatIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var builder = new StringBuilder(value.Length + 8);
        var previous = '\0';
        foreach (var character in value.Trim())
        {
            if (character is '_' or '-')
            {
                builder.Append(' ');
            }
            else
            {
                if (builder.Length > 0
                    && char.IsUpper(character)
                    && previous != '\0'
                    && previous != ' '
                    && !char.IsUpper(previous))
                {
                    builder.Append(' ');
                }

                builder.Append(character);
            }

            previous = character;
        }

        return builder.ToString();
    }

    internal static string FormatOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

public sealed class SettingsOperationsApprovalPolicyRow
{
    public SettingsOperationsApprovalPolicyRow(OperationsApprovalPolicyMatrixRowDto row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
    }

    public OperationsApprovalPolicyMatrixRowDto Row { get; }

    public string PolicyKey => Row.PolicyKey;

    public string WorkflowArea => FormatIdentifier(Row.WorkflowArea);

    public string Action => FormatIdentifier(Row.Action);

    public string GateLabel => FormatEnumLabel(Row.Gate);

    public string Trigger => FormatOptional(Row.Trigger);

    public string PermissionLabel => FormatOptional(Row.RequiredPermission);

    public string ReviewerLabel => $"{FormatOptional(Row.SubmitterRole)} -> {FormatOptional(Row.ReviewerRole)}";

    public string ApprovalRuleLabel => Row.RequiredDistinctApprovals == 1
        ? "1 distinct approval"
        : $"{Row.RequiredDistinctApprovals.ToString(CultureInfo.InvariantCulture)} distinct approvals";

    public string ControlLabel
    {
        get
        {
            var controls = new List<string>();
            if (Row.RequiresIndependentReviewer)
            {
                controls.Add("independent reviewer");
            }

            if (Row.RequiresReportPack)
            {
                controls.Add("report pack");
            }

            if (Row.RequiresChecklistControlApprovals)
            {
                controls.Add("checklist controls");
            }

            return controls.Count == 0 ? "standard evidence" : string.Join(", ", controls);
        }
    }

    public string EvidenceRequirement => FormatOptional(Row.EvidenceRequirement);

    public string AuditEventType => FormatIdentifier(Row.AuditEventType);

    public string Route => FormatOptional(Row.Route);

    public string Severity => FormatIdentifier(Row.Severity);

    private static string FormatEnumLabel<T>(T value) where T : struct, Enum
        => SettingsViewModel.FormatIdentifier(value.ToString());

    private static string FormatIdentifier(string? value)
        => SettingsViewModel.FormatIdentifier(value);

    private static string FormatOptional(string? value)
        => SettingsViewModel.FormatOptional(value);
}

public sealed class SettingsOperationsCloseCalendarRow
{
    public SettingsOperationsCloseCalendarRow(OperationsCloseCalendarItemDto item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public OperationsCloseCalendarItemDto Item { get; }

    public Guid WorkflowId => Item.WorkflowId;

    public Guid FundAccountId => Item.FundAccountId;

    public string PeriodId => Item.PeriodId;

    public string StatusLabel => SettingsViewModel.FormatIdentifier(Item.Status.ToString());

    public string DueLabel
    {
        get
        {
            var due = Item.NextDueDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "No due date";
            return string.IsNullOrWhiteSpace(Item.NextDueLabel) ? due : $"{due} - {Item.NextDueLabel}";
        }
    }

    public string OwnerLabel => SettingsViewModel.FormatOptional(Item.NextDueOwner);

    public string ReadinessLabel
    {
        get
        {
            var severity = SettingsViewModel.FormatOptional(Item.ReadinessSeverity);
            return Item.ReadinessScore.HasValue
                ? $"{Item.ReadinessScore.Value.ToString(CultureInfo.InvariantCulture)}/100 {SettingsViewModel.FormatIdentifier(severity)}"
                : SettingsViewModel.FormatIdentifier(severity);
        }
    }

    public bool IsReadyToClose => Item.IsReadyToClose;

    public int BlockerCount => Item.BlockerCount;

    public string BlockerLabel => Item.BlockerCount == 1 ? "1 blocker" : $"{Item.BlockerCount.ToString(CultureInfo.InvariantCulture)} blockers";

    public string ChecklistLabel => Item.OpenChecklistCount == 1
        ? "1 open checklist"
        : $"{Item.OpenChecklistCount.ToString(CultureInfo.InvariantCulture)} open checklists";

    public string ApprovalLabel => $"{Item.CompletedApprovalCount.ToString(CultureInfo.InvariantCulture)}/{Item.RequiredApprovalCount.ToString(CultureInfo.InvariantCulture)} approvals";

    public string Route => SettingsViewModel.FormatOptional(Item.Route);

    public string WorkflowIdLabel => Item.WorkflowId.ToString("D");

    public string FundAccountIdLabel => Item.FundAccountId.ToString("D");
}
