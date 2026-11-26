using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels;

/// <summary>An identifier kind offered by the historical window form, with its operator-facing label.</summary>
public sealed record HistoricalWindowKindOption(SecurityIdentifierKind Kind, string Label);

/// <summary>
/// Desktop parity for proposing a governed historical identifier window (browser passport editor's
/// "Propose historical identifier" section). Imports stamp identifier validity with the ingest time,
/// so an identifier the security carried earlier is staged here as a Draft revision; the editor's
/// existing Submit, Approve and Publish commands then carry it through the maker-checker gate. The
/// form pre-checks with the server's own proposal rules
/// (<see cref="SecurityMasterHistoricalIdentifierWindow.Validate"/>); the server re-validates.
/// </summary>
public sealed partial class SecurityPassportEditorViewModel
{
    private static readonly IReadOnlyDictionary<SecurityIdentifierKind, string> KindLabels =
        new Dictionary<SecurityIdentifierKind, string>
        {
            [SecurityIdentifierKind.Isin] = "ISIN",
            [SecurityIdentifierKind.Cusip] = "CUSIP",
            [SecurityIdentifierKind.Sedol] = "SEDOL",
            [SecurityIdentifierKind.Figi] = "FIGI",
            [SecurityIdentifierKind.OccOptionSymbol] = "OCC option symbol",
            [SecurityIdentifierKind.ProviderSymbol] = "Provider symbol",
            [SecurityIdentifierKind.InternalCode] = "Internal code",
            [SecurityIdentifierKind.Lei] = "LEI",
            [SecurityIdentifierKind.PermId] = "PermID",
            [SecurityIdentifierKind.Bbgid] = "BBGID",
            [SecurityIdentifierKind.Wkn] = "WKN",
            [SecurityIdentifierKind.PermTicker] = "Perm ticker",
            [SecurityIdentifierKind.Ric] = "RIC",
            [SecurityIdentifierKind.Cik] = "CIK",
            [SecurityIdentifierKind.LoanX] = "LoanX ID",
            [SecurityIdentifierKind.Clip] = "CLIP",
            [SecurityIdentifierKind.ListingId] = "Listing ID",
            [SecurityIdentifierKind.Uti] = "UTI",
            [SecurityIdentifierKind.Upi] = "UPI",
            [SecurityIdentifierKind.MarkitRedEntity] = "Markit RED entity",
            [SecurityIdentifierKind.MarkitRedPair] = "Markit RED pair",
        };

    /// <summary>Every proposable identifier kind (all but <see cref="SecurityIdentifierKind.Unknown"/>).</summary>
    public IReadOnlyList<HistoricalWindowKindOption> WindowKinds { get; } = Enum.GetValues<SecurityIdentifierKind>()
        .Where(static kind => kind != SecurityIdentifierKind.Unknown)
        .Select(static kind => new HistoricalWindowKindOption(kind, KindLabels.TryGetValue(kind, out var label) ? label : kind.ToString()))
        .ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowProviderLabel))]
    private SecurityIdentifierKind _windowKind = SecurityIdentifierKind.Ticker;

    [ObservableProperty] private string _windowValue = string.Empty;
    [ObservableProperty] private string _windowProvider = string.Empty;

    /// <summary>The date the identifier became valid (date-only; sent as UTC midnight).</summary>
    [ObservableProperty] private DateTime? _windowValidFrom;

    /// <summary>The date the identifier stopped being valid, or null if it is still valid.</summary>
    [ObservableProperty] private DateTime? _windowValidTo;

    [ObservableProperty] private string _windowEvidenceReference = string.Empty;
    [ObservableProperty] private string _windowJustification = string.Empty;

    public string WindowProviderLabel => WindowKind == SecurityIdentifierKind.ProviderSymbol
        ? "Provider (required)"
        : "Provider (optional)";

    /// <summary>
    /// Why the window cannot be staged right now, or null when it can. The editor tracks one working
    /// revision, so a new proposal waits until the current one is published or discarded.
    /// </summary>
    public string? WindowBlockedReason
    {
        get
        {
            if (!HasLoadedPassport)
            {
                return "Open a security passport to propose a historical window.";
            }

            if (HasActiveRevision)
            {
                return "Publish or discard the current revision before proposing a historical window.";
            }

            if (WindowValidFrom is null)
            {
                return "Enter the date the identifier became valid.";
            }

            return SecurityMasterHistoricalIdentifierWindow.Validate(
                WindowKind,
                WindowValue,
                WindowProvider,
                ToUtcMidnight(WindowValidFrom.Value),
                WindowValidTo is { } validTo ? ToUtcMidnight(validTo) : null,
                WindowEvidenceReference,
                WindowJustification,
                now: DateTimeOffset.UtcNow);
        }
    }

    private bool HasUnsavedWindowInput =>
        !string.IsNullOrWhiteSpace(WindowValue)
        || !string.IsNullOrWhiteSpace(WindowProvider)
        || WindowValidFrom is not null
        || WindowValidTo is not null
        || !string.IsNullOrWhiteSpace(WindowEvidenceReference)
        || !string.IsNullOrWhiteSpace(WindowJustification);

    private bool CanProposeHistoricalWindow() => !IsBusy && WindowBlockedReason is null;

    [RelayCommand(CanExecute = nameof(CanProposeHistoricalWindow))]
    private async Task ProposeHistoricalWindowAsync(CancellationToken ct)
    {
        var validFrom = ToUtcMidnight(WindowValidFrom!.Value);
        DateTimeOffset? validTo = WindowValidTo is { } end ? ToUtcMidnight(end) : null;
        await RunAsync("Staging historical identifier window…", () =>
        {
            var request = new ProposeHistoricalIdentifierWindowRequest(
                SecurityId: SecurityId,
                ExpectedVersion: Version,
                Kind: WindowKind,
                Value: WindowValue.Trim(),
                Provider: string.IsNullOrWhiteSpace(WindowProvider) ? null : WindowProvider.Trim(),
                ValidFrom: validFrom,
                ValidTo: validTo,
                EvidenceReference: WindowEvidenceReference.Trim(),
                Justification: WindowJustification.Trim(),
                Actor: string.Empty,
                FundProfileId: FundProfileId);
            return _client.ProposeHistoricalIdentifierWindowAsync(SecurityId, request, ct);
        }, ApplyHistoricalWindowResult, ct);
    }

    private void ApplyHistoricalWindowResult(SecurityMasterEditResultDto result)
    {
        ApplyEditResult(result);
        ResetWindowInputs();
        StatusText = $"Historical window staged as {result.State}. Submit it for approval and name an independent reviewer; it is applied when published.";
    }

    private void ResetWindowInputs()
    {
        WindowKind = SecurityIdentifierKind.Ticker;
        WindowValue = string.Empty;
        WindowProvider = string.Empty;
        WindowValidFrom = null;
        WindowValidTo = null;
        WindowEvidenceReference = string.Empty;
        WindowJustification = string.Empty;
    }

    /// <summary>A DatePicker date pinned to UTC midnight, so the server never shifts it across time zones.</summary>
    private static DateTimeOffset ToUtcMidnight(DateTime date)
        => new(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc));

    private void RefreshHistoricalWindowState()
    {
        OnPropertyChanged(nameof(WindowBlockedReason));
        ProposeHistoricalWindowCommand.NotifyCanExecuteChanged();
    }

    partial void OnWindowKindChanged(SecurityIdentifierKind value) => RefreshHistoricalWindowState();
    partial void OnWindowValueChanged(string value) => RefreshHistoricalWindowState();
    partial void OnWindowProviderChanged(string value) => RefreshHistoricalWindowState();
    partial void OnWindowValidFromChanged(DateTime? value) => RefreshHistoricalWindowState();
    partial void OnWindowValidToChanged(DateTime? value) => RefreshHistoricalWindowState();
    partial void OnWindowEvidenceReferenceChanged(string value) => RefreshHistoricalWindowState();
    partial void OnWindowJustificationChanged(string value) => RefreshHistoricalWindowState();
    partial void OnSecurityIdChanged(Guid value) => RefreshHistoricalWindowState();
    partial void OnRevisionIdChanged(Guid? value) => RefreshHistoricalWindowState();
    partial void OnRevisionStateChanged(SecurityMasterRevisionStateDto? value) => RefreshHistoricalWindowState();
    partial void OnIsBusyChanged(bool value) => RefreshHistoricalWindowState();
}
