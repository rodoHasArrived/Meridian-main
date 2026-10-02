using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>Desktop presentation of the same subject-addressed amount proof packet used by the browser.</summary>
public sealed class LedgerAmountProofDrawerViewModel : BindableBase
{
    private readonly ILedgerReportsApiClient? _client;
    private int _revision;
    private bool _isOpen;
    private bool _isLoading;
    private string _title = "Amount proof";
    private string _statusText = "Review required";
    private string _scopeText = string.Empty;
    private string _subjectId = string.Empty;
    private string _manifestText = string.Empty;
    private LedgerAmountScopeDto? _scope;
    private readonly Dictionary<string, string> _artifactSubjects = new(StringComparer.Ordinal);

    public LedgerAmountProofDrawerViewModel(ILedgerReportsApiClient? client)
    {
        _client = client;
        CloseCommand = new RelayCommand(Close);
        OpenManifestCommand = new AsyncRelayCommand<LedgerAmountProofEvidenceDto>(OpenManifestAsync, CanOpenManifest);
    }

    public IRelayCommand CloseCommand { get; }
    public IAsyncRelayCommand<LedgerAmountProofEvidenceDto> OpenManifestCommand { get; }
    public ObservableCollection<LedgerAmountProofEvidenceDto> Evidence { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ScopeText { get => _scopeText; private set => SetProperty(ref _scopeText, value); }
    public string SubjectId { get => _subjectId; private set => SetProperty(ref _subjectId, value); }
    public string ManifestText { get => _manifestText; private set => SetProperty(ref _manifestText, value); }

    public void Close()
    {
        _revision++;
        IsOpen = false;
        IsLoading = false;
        Evidence.Clear();
        Warnings.Clear();
        ScopeText = string.Empty;
        SubjectId = string.Empty;
        ManifestText = string.Empty;
        _scope = null;
        _artifactSubjects.Clear();
        OpenManifestCommand.NotifyCanExecuteChanged();
    }

    public void ShowBlocked(string reason)
    {
        Close();
        IsOpen = true;
        Title = "Amount proof";
        StatusText = "Blocked";
        Warnings.Add(reason);
    }

    public async Task OpenAsync(PostedLedgerAmountSelection selection, CancellationToken ct = default)
    {
        Close();
        var revision = _revision;
        IsOpen = true;
        Title = $"{selection.AccountName} · {selection.Side} {PostedLedgerProjection.FormatAmount(selection.Amount, selection.Currency)}";
        SubjectId = selection.SubjectId;
        StatusText = "Review required · loading retained evidence";
        if (_client is null || string.IsNullOrWhiteSpace(selection.FundProfileId) ||
            selection.LedgerBookId == Guid.Empty || selection.PeriodId == Guid.Empty)
        {
            StatusText = "Blocked";
            Warnings.Add("The selected amount has no authoritative fund, book, or period evidence scope.");
            return;
        }

        IsLoading = true;
        try
        {
            var response = await _client.GetAmountProofAsync(
                selection.SubjectId, selection.LedgerBookId, selection.PeriodId, selection.FundProfileId, ct).ConfigureAwait(true);
            if (revision != _revision || ct.IsCancellationRequested)
            {
                return;
            }

            var packet = response.Data;
            var proof = packet?.LedgerAmount;
            if (!response.Success || proof is null)
            {
                StatusText = "Blocked";
                Warnings.Add("Retained evidence for this amount is unavailable. Refresh after its evidence is retained and scoped.");
                return;
            }

            if (packet!.Subject.SubjectKind != "ledger-amount" || packet.Subject.SubjectId != selection.SubjectId ||
                proof.SubjectId != selection.SubjectId || proof.Scope.LedgerBookId != selection.LedgerBookId ||
                proof.Scope.PeriodId != selection.PeriodId || proof.Scope.FundProfileId != selection.FundProfileId ||
                string.IsNullOrWhiteSpace(proof.Scope.TenantId) || string.IsNullOrWhiteSpace(proof.Scope.CompanyId) ||
                proof.Amount != selection.Amount || proof.Currency != selection.Currency)
            {
                StatusText = "Blocked";
                Warnings.Add("The evidence response does not match the selected amount and its exact scope.");
                return;
            }

            ScopeText = $"Tenant {proof.Scope.TenantId} · company {proof.Scope.CompanyId} · fund {proof.Scope.FundProfileId}"
                + $" · book {proof.Scope.LedgerBookId:D} · period {proof.Scope.PeriodId:D}";
            foreach (var warning in proof.Warnings)
            {
                Warnings.Add(warning);
            }

            if (proof.Status == EvidenceStatusDto.Blocked || packet.Completeness.Status == EvidenceStatusDto.Blocked ||
                proof.Evidence.Any(item => item.Status == EvidenceStatusDto.Blocked))
            {
                StatusText = "Blocked";
                if (Warnings.Count == 0)
                {
                    Warnings.Add("The shared evidence service blocked this amount's supporting evidence.");
                }
                return;
            }

            var duplicateIds = proof.Evidence.GroupBy(item => item.EvidenceId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);
            if (duplicateIds)
            {
                StatusText = "Blocked";
                Warnings.Add("The evidence response contains ambiguous duplicate identifiers.");
                return;
            }

            foreach (var item in proof.Evidence)
            {
                if (item.Status == EvidenceStatusDto.Ready && item.RetainedAt is not null &&
                    item.RetainedAt <= DateTimeOffset.UtcNow &&
                    !string.IsNullOrWhiteSpace(item.EvidenceId))
                {
                    Evidence.Add(item);
                }
                else
                {
                    Warnings.Add(item.Reason ?? $"Evidence {item.EvidenceId} is missing, stale, or requires review.");
                }
            }

            StatusText = proof.Status == EvidenceStatusDto.Ready && packet.Completeness.Status == EvidenceStatusDto.Ready &&
                         Evidence.Any(item => item.Kind != "ledger-record") && Evidence.Count == proof.Evidence.Count
                ? "Ready"
                : "Review required";
            if (!Evidence.Any(item => item.Kind != "ledger-record"))
            {
                Warnings.Add("No retained supporting evidence is available for this amount.");
            }
            _scope = proof.Scope;
            var retainedSubject = $"{Uri.EscapeDataString(proof.Scope.FundProfileId)}:{proof.Scope.LedgerBookId:D}:{proof.Scope.PeriodId:D}:{proof.SubjectId}";
            foreach (var item in Evidence)
            {
                if (item.Kind == "ledger-record")
                {
                    continue;
                }
                var nodes = packet.Nodes.Where(node => node?.EvidenceId == item.EvidenceId).ToArray();
                if (nodes.Length != 1 || nodes[0].Subject?.SubjectKind != "ledger-amount" ||
                    nodes[0].Subject?.SubjectId != proof.SubjectId || nodes[0].ArtifactRefs is null)
                {
                    ShowBlocked("Supporting evidence does not identify this exact retained amount and scope.");
                    return;
                }
                var artifacts = nodes[0].ArtifactRefs.Where(artifact => artifact?.ArtifactId == item.EvidenceId).ToArray();
                if (artifacts.Length != 1 || artifacts[0].Kind != item.Kind || !artifacts[0].Retained ||
                    artifacts[0].Route != item.Route || artifacts[0].CanonicalSubjectKind != "ledger-amount" ||
                    artifacts[0].CanonicalSubjectId != retainedSubject || artifacts[0].GeneratedAt != item.RetainedAt ||
                    !Sha256Digest.FixedEquals(artifacts[0].Hash, item.ContentHash) || !ManifestRouteMatchesScope(item))
                {
                    ShowBlocked("Supporting evidence does not identify this exact retained amount and scope.");
                    return;
                }
                _artifactSubjects[item.EvidenceId] = artifacts[0].CanonicalSubjectId!;
            }
            OpenManifestCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (revision == _revision)
            {
                Close();
            }
        }
        catch (Exception)
        {
            if (revision == _revision)
            {
                Evidence.Clear();
                StatusText = "Blocked";
                Warnings.Add("The amount evidence request failed. Refresh to retry the retained evidence read.");
            }
        }
        finally
        {
            if (revision == _revision)
            {
                IsLoading = false;
            }
        }
    }

    private bool CanOpenManifest(LedgerAmountProofEvidenceDto? item)
        => IsOpen && _scope is not null && item is not null && Evidence.Contains(item) &&
           Sha256Digest.IsWellFormed(item.ContentHash) &&
           _artifactSubjects.ContainsKey(item.EvidenceId) && ManifestRouteMatchesScope(item);

    private bool ManifestRouteMatchesScope(LedgerAmountProofEvidenceDto item)
        => _scope is not null && LedgerReportsApiClient.TryGetRetainedManifestScope(item.Route, out var query) &&
           query["ledgerAmountSubjectId"] == SubjectId && query["ledgerBookId"] == _scope.LedgerBookId.ToString("D") &&
           query["periodId"] == _scope.PeriodId.ToString("D") && query["fundProfileId"] == _scope.FundProfileId &&
           Sha256Digest.FixedEquals(query["expectedContentHash"], item.ContentHash);

    private async Task OpenManifestAsync(LedgerAmountProofEvidenceDto? item)
    {
        if (!CanOpenManifest(item) || _client is null)
        {
            return;
        }

        var revision = _revision;
        var scope = _scope!;
        var retainedSubject = _artifactSubjects[item!.EvidenceId];
        ManifestText = string.Empty;
        try
        {
            var response = await _client.GetRetainedManifestAsync(item.Route!).ConfigureAwait(true);
            using var document = response.Data;
            if (revision != _revision)
            {
                return;
            }
            var manifest = document?.RootElement ?? default;
            if (!response.Success || manifest.ValueKind != JsonValueKind.Object ||
                Text(manifest, "tenantId") != scope.TenantId || Text(manifest, "scope") != scope.CompanyId ||
                !manifest.TryGetProperty("subject", out var subject) || Text(subject, "subjectKind") != "ledger-amount" ||
                Text(subject, "subjectId") != retainedSubject ||
                !manifest.TryGetProperty("vaultIdentity", out var identity) ||
                !Sha256Digest.FixedEquals(Text(identity, "contentHashSha256"), item.ContentHash))
            {
                ShowBlocked("The retained manifest is unavailable or does not match this amount's verified scope and digest.");
                return;
            }
            ManifestText = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception)
        {
            if (revision == _revision)
            {
                ShowBlocked("The retained manifest could not be read. Refresh the amount proof before retrying.");
            }
        }

        static string? Text(JsonElement element, string key)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
