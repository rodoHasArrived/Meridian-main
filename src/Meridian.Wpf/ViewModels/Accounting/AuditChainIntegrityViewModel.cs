using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>
/// Desktop parity for the browser's Accounting → Configuration audit-chain integrity check
/// (W9-GOV-008 verification tooling). Verification walks every retained chain end to end, so it
/// runs only on demand. Each chain keeps its own answer: a chain this deployment does not keep, or
/// one with nothing chained yet, is not evidence of integrity and a store that could not be read is not evidence of tampering, so
/// an unchecked chain is never rolled into a passing verdict.
/// </summary>
public sealed class AuditChainIntegrityViewModel : BindableBase
{
    private const string IdleStatus = "Not yet checked. Verification re-reads every chained entry and appends nothing.";

    private readonly IAccountingAuditIntegrityApiClient? _client;
    private CancellationTokenSource? _inFlight;
    private string _statusText = IdleStatus;
    private string _verdictTitle = string.Empty;
    private string _verdictDetail = string.Empty;
    private string _verdictTone = "default";
    private bool _isBusy;

    public AuditChainIntegrityViewModel(IAccountingAuditIntegrityApiClient? client)
    {
        _client = client;
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => _client is not null && !IsBusy);
        if (client is null)
        {
            _statusText = "Audit chain verification is unavailable: the workstation API client is not registered.";
        }
    }

    public IAsyncRelayCommand VerifyCommand { get; }

    /// <summary>One row per chain: Name = chain, Status = verdict, Detail = coverage, Evidence = failure.</summary>
    public ObservableCollection<AccountingWorkbenchRow> Rows { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string VerdictTitle
    {
        get => _verdictTitle;
        private set => SetProperty(ref _verdictTitle, value);
    }

    public string VerdictDetail
    {
        get => _verdictDetail;
        private set => SetProperty(ref _verdictDetail, value);
    }

    /// <summary><c>success</c>, <c>danger</c>, <c>warning</c>, or <c>default</c>, matching the browser panel.</summary>
    public string VerdictTone
    {
        get => _verdictTone;
        private set => SetProperty(ref _verdictTone, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                VerifyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Abandons a running verification, e.g. when the page unloads, so the server stops scanning
    /// and releases the chain-head locks and its single-flight slot.
    /// </summary>
    public void Cancel() => _inFlight?.Cancel();

    public async Task VerifyAsync()
    {
        if (_client is null)
        {
            return;
        }

        _inFlight?.Cancel();
        using var cts = new CancellationTokenSource();
        _inFlight = cts;
        IsBusy = true;
        // A fresh check was asked for, so the previous verdict no longer stands while it runs.
        ClearVerdict();
        StatusText = "Verifying the accounting and ledger audit chains…";
        try
        {
            var result = await _client.VerifyAsync(cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Apply(result);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Abandoned by a newer run or by leaving the page; nothing to report.
        }
        catch (Exception ex)
        {
            ClearVerdict();
            StatusText = $"Audit integrity check failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_inFlight, cts))
            {
                _inFlight = null;
                IsBusy = false;
            }
        }
    }

    private void Apply(AuditIntegrityCheckResult result)
    {
        switch (result.Outcome)
        {
            case AuditIntegrityCheckOutcome.Completed when result.Report is { } report:
                ShowReport(report);
                break;
            case AuditIntegrityCheckOutcome.Forbidden:
                // A refusal is not a verification failure: say why nothing was checked.
                ClearVerdict();
                StatusText = "Requires maintenance authority: audit chain verification spans every tenant's records, so it is limited to operators with AdminMaintenance.";
                break;
            case AuditIntegrityCheckOutcome.Busy:
                ClearVerdict();
                StatusText = "Verification already running: another audit chain verification is in progress. Retry when it completes.";
                break;
            case AuditIntegrityCheckOutcome.RateLimited:
                ClearVerdict();
                StatusText = "Rate limited: the workstation's shared request budget is exhausted. Retry in a moment.";
                break;
            default:
                ClearVerdict();
                StatusText = $"Audit integrity check failed: {result.Error ?? "no report was returned"}.";
                break;
        }
    }

    private void ShowReport(AccountingAuditIntegrityReportDto report)
    {
        var rows = report.Chains.Select(BuildRow).ToList();
        Rows.ReplaceWith(rows);

        var broken = report.Chains.Where(chain => chain.Status == AuditChainIntegrityStatus.Broken).Select(chain => ChainLabel(chain.Chain)).ToList();
        var unavailable = report.Chains.Where(chain => chain.Status == AuditChainIntegrityStatus.Unavailable).Select(chain => ChainLabel(chain.Chain)).ToList();
        var notStarted = report.Chains.Where(chain => chain.Status == AuditChainIntegrityStatus.NotStarted).Select(chain => ChainLabel(chain.Chain)).ToList();
        var configured = report.Chains.Count(chain => chain.Status != AuditChainIntegrityStatus.NotConfigured);
        var anyVerified = report.Chains.Any(chain => chain.Status == AuditChainIntegrityStatus.Verified);

        if (broken.Count > 0)
        {
            SetVerdict("danger", "Audit chain integrity failure",
                $"{string.Join(" and ", broken)} did not verify. Retained history may have been altered, truncated, or interrupted mid-append; investigate before relying on it.");
        }
        else if (unavailable.Count > 0)
        {
            SetVerdict("warning", anyVerified ? "Audit chains partly checked" : "No audit chain verified",
                $"{string.Join(" and ", unavailable)} could not be read, so nothing is claimed about {(unavailable.Count == 1 ? "it" : "them")}.");
        }
        else if (notStarted.Count > 0)
        {
            SetVerdict("warning", anyVerified ? "Audit chains partly checked" : "No audit chain verified",
                $"{string.Join(" and ", notStarted)} {(notStarted.Count == 1 ? "has" : "have")} not begun chaining, so none of {(notStarted.Count == 1 ? "its" : "their")} records are protected yet.");
        }
        else if (configured == 0 || !report.AllVerified)
        {
            SetVerdict("warning", "No audit chain verified",
                "This deployment keeps no hash-chained audit log that could be checked.");
        }
        else
        {
            SetVerdict("success", "Audit chains verified",
                $"Every configured audit chain verified ({configured} of {report.Chains.Count}).");
        }

        StatusText = $"Verification ran {report.VerifiedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}.";
    }

    private static AccountingWorkbenchRow BuildRow(AuditChainIntegrityResultDto result) => new(
        ChainLabel(result.Chain),
        result.Status switch
        {
            AuditChainIntegrityStatus.Verified => "Verified",
            AuditChainIntegrityStatus.Broken => "Broken",
            AuditChainIntegrityStatus.Unavailable => "Not checked — store unavailable",
            AuditChainIntegrityStatus.NotConfigured => "Not kept in this deployment",
            AuditChainIntegrityStatus.NotStarted => "Nothing chained yet",
            _ => result.Status,
        },
        DescribeCoverage(result),
        DescribeFailure(result) ?? string.Empty,
        result.Chain);

    private static string ChainLabel(string chain) => chain switch
    {
        AuditChainIdentifiers.AccountingActions => "Accounting actions",
        AuditChainIdentifiers.LedgerEvents => "Ledger events",
        _ => chain,
    };

    private static string DescribeCoverage(AuditChainIntegrityResultDto result)
    {
        if (result.Status is AuditChainIntegrityStatus.NotConfigured or AuditChainIntegrityStatus.Unavailable)
        {
            return result.Detail ?? "—";
        }

        var links = string.Create(CultureInfo.InvariantCulture,
            $"{result.LinksChecked:N0} chained {(result.LinksChecked == 1 ? "entry" : "entries")} checked");
        return result.UnprotectedPreChainCount > 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"{links}; {result.UnprotectedPreChainCount:N0} earlier {(result.UnprotectedPreChainCount == 1 ? "record predates" : "records predate")} chaining and are not covered")
            : links;
    }

    private static string? DescribeFailure(AuditChainIntegrityResultDto result)
    {
        if (result.Status != AuditChainIntegrityStatus.Broken)
        {
            return null;
        }

        var position = result.FailedSequence is { } sequence
            ? string.Create(CultureInfo.InvariantCulture, $" at sequence {sequence}")
            : string.Empty;
        var detail = string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : $" — {result.Detail}";
        return $"{result.FailureKind ?? "Unclassified"}{position}{detail}";
    }

    private void SetVerdict(string tone, string title, string detail)
    {
        VerdictTone = tone;
        VerdictTitle = title;
        VerdictDetail = detail;
    }

    private void ClearVerdict()
    {
        Rows.Clear();
        SetVerdict("default", string.Empty, string.Empty);
    }
}
