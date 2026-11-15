using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public sealed partial class IntercompanyAgreementWorkspace
{
    private bool _useStructuredTerms, _useExternalReview;
    private string _scheduleStart = "", _scheduleEnd = "", _calendar = "", _floatingIndex = "", _spread = "", _feeAmount = "", _feeRate = "", _feeBase = "", _driver = "", _beneficiary = "", _guaranteeLimit = "";
    private int _businessDays, _expiryDays = 30;
    private IntercompanyRateBasisDto _rateBasis;
    private IntercompanyFrequencyDto _frequency;
    private IntercompanyDayCountDto _dayCount;
    private IntercompanyCompoundingDto _compounding;
    private BusinessDayConvention _businessDayConvention;
    private string _externalReviewer = "", _reviewerReference = "", _reviewedOn = "", _reviewEvidence = "", _reviewArtifact = "", _reviewEvidenceHash = "";
    private string _datePreviewStatus = "Preview retained terms to inspect dates. No charge amounts or journals are calculated.";
    private string _obligationStatus = "Not evaluated. Intercompany observations are advisory and do not change close eligibility.";
    private IntercompanyObligationMonitorDto? _obligationMonitor;
    private IntercompanyAllocationEditor? _selectedAllocation;
    private IntercompanyAgreementDetailDto? _selectedGuaranteeDisclosure;
    private IntercompanyObligationFindingDto? _selectedObligationFinding;

    public IReadOnlyList<IntercompanyRateBasisDto> RateBases { get; } = Enum.GetValues<IntercompanyRateBasisDto>();
    public IReadOnlyList<IntercompanyFrequencyDto> Frequencies { get; } = Enum.GetValues<IntercompanyFrequencyDto>();
    public IReadOnlyList<IntercompanyDayCountDto> DayCounts { get; } = Enum.GetValues<IntercompanyDayCountDto>();
    public IReadOnlyList<IntercompanyCompoundingDto> CompoundingChoices { get; } = Enum.GetValues<IntercompanyCompoundingDto>();
    public IReadOnlyList<BusinessDayConvention> BusinessDayConventions { get; } = Enum.GetValues<BusinessDayConvention>();
    public ObservableCollection<IntercompanyAllocationEditor> StructuredAllocations { get; } = [];
    public ObservableCollection<IntercompanyDatePeriodDto> DatePreviewPeriods { get; } = [];
    public ObservableCollection<IntercompanyObligationFindingDto> ObligationFindings { get; } = [];
    public ObservableCollection<IntercompanyAgreementDetailDto> GuaranteeDisclosures { get; } = [];
    public ObservableCollection<IntercompanyBalanceScheduleRowDto> ObligationBalances { get; } = [];
    public bool UseStructuredTerms { get => _useStructuredTerms; set { if (SetProperty(ref _useStructuredTerms, value) && IsEditing) { IsDirty = true; InvalidateDatePreview(); } OnPropertyChanged(nameof(TermsFormatLabel)); } }
    public string RetainedStructuredTermsSummary
    {
        get
        {
            var pending = Detail?.Proposals.LastOrDefault(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted);
            return StructuredSummary(pending is not null ? pending.Draft.Terms?.StructuredTerms : Detail?.ApplicableRevision?.Terms.Terms?.StructuredTerms);
        }
    }
    public string TermsFormatLabel => UseStructuredTerms ? "StructuredV1 — declared inputs only; no automatic accrual or posting." : "Unstructured — legal text is retained without inferred calculation inputs.";
    public bool UseExternalReview { get => _useExternalReview; set { if (SetProperty(ref _useExternalReview, value)) NotifyCommands(); } }
    public string ScheduleStart { get => _scheduleStart; set => Edit(ref _scheduleStart, value); }
    public string ScheduleEnd { get => _scheduleEnd; set => Edit(ref _scheduleEnd, value); }
    public string CalendarId { get => _calendar; set => Edit(ref _calendar, value); }
    public string FloatingIndexReference { get => _floatingIndex; set => Edit(ref _floatingIndex, value); }
    public string SpreadBps { get => _spread; set => Edit(ref _spread, value); }
    public string FixedFeeAmount { get => _feeAmount; set => Edit(ref _feeAmount, value); }
    public string AnnualFeeRate { get => _feeRate; set => Edit(ref _feeRate, value); }
    public string CalculationBaseReference { get => _feeBase; set => Edit(ref _feeBase, value); }
    public string AllocationDriverReference { get => _driver; set => Edit(ref _driver, value); }
    public string BeneficiaryReference { get => _beneficiary; set => Edit(ref _beneficiary, value); }
    public string GuaranteeLimit { get => _guaranteeLimit; set => Edit(ref _guaranteeLimit, value); }
    public int SettlementBusinessDays { get => _businessDays; set => TypedEdit(ref _businessDays, value); }
    public IntercompanyRateBasisDto RateBasis { get => _rateBasis; set => TypedEdit(ref _rateBasis, value); }
    public IntercompanyFrequencyDto Frequency { get => _frequency; set => TypedEdit(ref _frequency, value); }
    public IntercompanyDayCountDto DayCount { get => _dayCount; set => TypedEdit(ref _dayCount, value); }
    public IntercompanyCompoundingDto Compounding { get => _compounding; set => TypedEdit(ref _compounding, value); }
    public BusinessDayConvention BusinessDayConvention { get => _businessDayConvention; set => TypedEdit(ref _businessDayConvention, value); }
    public string ExternalReviewer { get => _externalReviewer; set => SetProperty(ref _externalReviewer, value); }
    public string ReviewerReference { get => _reviewerReference; set => SetProperty(ref _reviewerReference, value); }
    public string ReviewedOn { get => _reviewedOn; set => SetProperty(ref _reviewedOn, value); }
    public string ReviewEvidenceReference { get => _reviewEvidence; set => SetProperty(ref _reviewEvidence, value); }
    public string ReviewEvidenceArtifact { get => _reviewArtifact; set => SetProperty(ref _reviewArtifact, value); }
    public string ReviewEvidenceHash { get => _reviewEvidenceHash; set => SetProperty(ref _reviewEvidenceHash, value); }
    public string DatePreviewStatus { get => _datePreviewStatus; private set => SetProperty(ref _datePreviewStatus, value); }
    public string ObligationStatus { get => _obligationStatus; private set => SetProperty(ref _obligationStatus, value); }
    public int ExpiryHorizonDays { get => _expiryDays; set { if (SetProperty(ref _expiryDays, value)) ResetObligationMonitor(); } }
    public IntercompanyAllocationEditor? SelectedStructuredAllocation { get => _selectedAllocation; set => SetProperty(ref _selectedAllocation, value); }
    public IntercompanyObligationMonitorDto? ObligationMonitor { get => _obligationMonitor; private set => SetProperty(ref _obligationMonitor, value); }
    public IntercompanyAgreementDetailDto? SelectedGuaranteeDisclosure { get => _selectedGuaranteeDisclosure; set { SetProperty(ref _selectedGuaranteeDisclosure, value); NotifyStructuredCommands(); } }
    public IntercompanyObligationFindingDto? SelectedObligationFinding { get => _selectedObligationFinding; set { SetProperty(ref _selectedObligationFinding, value); NotifyStructuredCommands(); } }
    public IAsyncRelayCommand OpenGuaranteeDisclosureCommand { get; private set; } = null!;
    public IAsyncRelayCommand InspectObligationAgreementCommand { get; private set; } = null!;
    public IRelayCommand AddStructuredAllocationCommand { get; private set; } = null!;
    public IRelayCommand RemoveStructuredAllocationCommand { get; private set; } = null!;
    public IAsyncRelayCommand PreviewProposalDatesCommand { get; private set; } = null!;
    public IAsyncRelayCommand PreviewRevisionDatesCommand { get; private set; } = null!;
    public IAsyncRelayCommand EvaluateObligationsCommand { get; private set; } = null!;

    private void InitializeStructuredCommands()
    {
        InitializeLegacyCoverageCommands();
        OpenGuaranteeDisclosureCommand = new AsyncRelayCommand(() => OpenMonitorAgreementAsync(SelectedGuaranteeDisclosure?.AgreementId), () => !IsBusy && !IsDirty && SelectedGuaranteeDisclosure is not null);
        InspectObligationAgreementCommand = new AsyncRelayCommand(() => OpenMonitorAgreementAsync(SelectedObligationFinding?.AgreementId), () => !IsBusy && !IsDirty && SelectedObligationFinding?.AgreementId is not null);
        AddStructuredAllocationCommand = new RelayCommand(() => { var row = new IntercompanyAllocationEditor(); row.PropertyChanged += AllocationChanged; StructuredAllocations.Add(row); IsDirty = true; }, () => !IsBusy && IsEditing);
        RemoveStructuredAllocationCommand = new RelayCommand(() => { if (SelectedStructuredAllocation is not null) { SelectedStructuredAllocation.PropertyChanged -= AllocationChanged; StructuredAllocations.Remove(SelectedStructuredAllocation); IsDirty = true; InvalidateDatePreview(); } }, () => !IsBusy && IsEditing);
        PreviewProposalDatesCommand = new AsyncRelayCommand(() => PreviewDatesAsync(false), () => !IsBusy && !IsDirty && Detail?.Proposals.Any() == true);
        PreviewRevisionDatesCommand = new AsyncRelayCommand(() => PreviewDatesAsync(true), () => !IsBusy && !IsDirty && SelectedRevision is not null);
        EvaluateObligationsCommand = new AsyncRelayCommand(EvaluateObligationsAsync, () => !IsBusy && !IsDirty && !string.IsNullOrWhiteSpace(LedgerBook));
    }
    private void TypedEdit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    { if (SetProperty(ref field, value, property) && IsEditing) { IsDirty = true; InvalidateDatePreview(); } }
    private static decimal? DecimalValue(string text, string label)
        => string.IsNullOrWhiteSpace(text) ? null : decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value : throw new InvalidOperationException($"{label} must be an exact decimal using . as separator.");
    private static string Value(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private IntercompanyStructuredTermsDto? BuildStructuredTerms()
    {
        if (!UseStructuredTerms)
            return null;
        if (AgreementType == IntercompanyAgreementTypeDto.Guarantee)
            return new(Guarantee: new(BeneficiaryReference, DecimalValue(GuaranteeLimit, "Guarantee limit")));
        var schedule = new IntercompanyScheduleDto(Date(ScheduleStart, "Schedule start"), Date(ScheduleEnd, "Schedule end"), Frequency, CalendarId, BusinessDayConvention, SettlementBusinessDays);
        return AgreementType switch
        {
            IntercompanyAgreementTypeDto.Loan => new(schedule, RateBasis, RateBasis == IntercompanyRateBasisDto.Fixed ? DecimalValue(FixedAnnualRate, "Annual rate") : null,
                RateBasis == IntercompanyRateBasisDto.Floating ? FloatingIndexReference : null, RateBasis == IntercompanyRateBasisDto.Floating ? DecimalValue(SpreadBps, "Spread basis points") : null, DayCount, Compounding),
            IntercompanyAgreementTypeDto.ManagementFee => new(schedule, FixedFeeAmount: DecimalValue(FixedFeeAmount, "Fixed fee amount"), AnnualFeeRate: DecimalValue(AnnualFeeRate, "Annual fee rate"), CalculationBaseReference: OptionalText(CalculationBaseReference)),
            IntercompanyAgreementTypeDto.CostShare => new(schedule, Allocations: string.IsNullOrWhiteSpace(AllocationDriverReference)
                ? StructuredAllocations.Select(x => new IntercompanyAllocationDto(ParseId(x.EntityId, "Allocation entity"), DecimalValue(x.Percent, "Allocation percent") ?? throw new InvalidOperationException("Allocation percentage is required."))).ToArray() : null,
                AllocationDriverReference: OptionalText(AllocationDriverReference)),
            _ => throw new InvalidOperationException("This agreement type supports unstructured legal terms only.")
        };
    }
    private static string? OptionalText(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
    private void FillStructuredTerms(IntercompanyStructuredTermsDto? terms)
    {
        UseStructuredTerms = terms is not null;
        var schedule = terms?.Schedule;
        ScheduleStart = schedule?.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        ScheduleEnd = schedule?.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        Frequency = schedule?.Frequency ?? IntercompanyFrequencyDto.Monthly;
        CalendarId = schedule?.CalendarId ?? "";
        BusinessDayConvention = schedule?.BusinessDayConvention ?? BusinessDayConvention.Following;
        SettlementBusinessDays = schedule?.SettlementBusinessDays ?? 0;
        RateBasis = terms?.RateBasis ?? IntercompanyRateBasisDto.Fixed;
        if (terms?.FixedAnnualRate is { } rate)
            FixedAnnualRate = Value(rate);
        FloatingIndexReference = terms?.FloatingIndexReference ?? "";
        SpreadBps = Value(terms?.SpreadBps);
        DayCount = terms?.DayCount ?? IntercompanyDayCountDto.Actual365;
        Compounding = terms?.Compounding ?? IntercompanyCompoundingDto.Simple;
        FixedFeeAmount = Value(terms?.FixedFeeAmount);
        AnnualFeeRate = Value(terms?.AnnualFeeRate);
        CalculationBaseReference = terms?.CalculationBaseReference ?? "";
        AllocationDriverReference = terms?.AllocationDriverReference ?? "";
        BeneficiaryReference = terms?.Guarantee?.BeneficiaryReference ?? "";
        GuaranteeLimit = Value(terms?.Guarantee?.LimitAmount);
        foreach (var row in StructuredAllocations)
            row.PropertyChanged -= AllocationChanged;
        StructuredAllocations.Clear();
        foreach (var allocation in terms?.Allocations ?? [])
        { var row = new IntercompanyAllocationEditor { EntityId = allocation.EntityId.ToString("D"), Percent = Value(allocation.Percent) }; row.PropertyChanged += AllocationChanged; StructuredAllocations.Add(row); }
        DatePreviewPeriods.Clear();
        DatePreviewStatus = "Preview retained terms to inspect dates. No amounts or journals are calculated.";
    }
    private void AllocationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (IsEditing) { IsDirty = true; InvalidateDatePreview(); } }
    private IntercompanyExternalReviewDto BuildExternalReview(string submittedHash)
        => new(ExternalReviewer, ReviewerReference, Date(ReviewedOn, "External review date"),
            new(ReviewEvidenceReference, OptionalText(ReviewEvidenceArtifact), OptionalText(ReviewEvidenceHash)), Rationale, submittedHash);
    private void InvalidateDatePreview()
    { DatePreviewPeriods.Clear(); DatePreviewStatus = "Local edits differ from retained terms. Save before requesting a new date preview."; }
    private async Task PreviewDatesAsync(bool approved)
    {
        if (Detail is not { } detail)
            return;
        await RunAsync(async token =>
        {
            DatePreviewPeriods.Clear();
            DatePreviewStatus = "Loading date evidence for the exact retained terms…";
            var proposal = detail.Proposals.LastOrDefault();
            var response = await _client.PreviewStructuredTermsAsync(detail.AgreementId, Date(AsOf, "As-of date"), approved ? SelectedRevision?.AgreementVersion : null, approved ? null : proposal?.ProposalId, token);
            token.ThrowIfCancellationRequested();
            var preview = Require(response);
            if (!approved && proposal?.SubmittedHash is { Length: > 0 } hash && hash != preview.SubjectHash)
                throw new InvalidOperationException("The preview does not match the frozen submitted proposal hash.");
            if (approved && SelectedRevision?.PayloadHash != preview.SubjectHash)
                throw new InvalidOperationException("The preview does not match the selected legal revision hash.");
            DatePreviewPeriods.Clear();
            foreach (var period in preview.Periods)
                DatePreviewPeriods.Add(period);
            DatePreviewStatus = $"{preview.Status} · {preview.TermsFormat} · kernel {preview.KernelVersion} · calendar {preview.CalendarId ?? "unavailable"} · {preview.CalendarHash ?? "no hash"}\nLegal subject {preview.SubjectHash}\n{string.Join(" ", preview.Diagnostics)}";
        });
    }
    private async Task EvaluateObligationsAsync()
    {
        await RunAsync(async token =>
        {
            ResetObligationMonitor();
            ObligationStatus = "Evaluating posted balances and legal evidence…";
            try
            {
                var entity = string.IsNullOrWhiteSpace(EntityFilter) ? (Guid?)null : ParseId(EntityFilter, "Entity filter");
                var request = new IntercompanyObligationMonitorRequestDto(null, ParseId(LedgerBook, "Ledger book"), Date(AsOf, "As-of date"), ExpiryHorizonDays, entity);
                var result = Require(await _client.GetObligationMonitorAsync(request, token));
                token.ThrowIfCancellationRequested();
                ObligationMonitor = result;
                foreach (var finding in result.Findings)
                    ObligationFindings.Add(finding);
                foreach (var guarantee in result.Guarantees)
                    GuaranteeDisclosures.Add(guarantee);
                foreach (var balance in result.BalanceSchedule?.Rows ?? [])
                    ObligationBalances.Add(balance);
                ObligationStatus = $"{result.Status} · {(result.IsComplete ? "complete observation" : "incomplete evidence")} · cutoff {result.AsOf:yyyy-MM-dd} · evaluated {result.EvaluatedAtUtc:O}\nSource {result.SourceFingerprint}\nAdvisory only. Close readiness, posting and settlement authority are unchanged.";
            }
            catch { if (!token.IsCancellationRequested) ObligationStatus = "Unavailable. Agreement coverage and lifecycle conclusions remain unknown."; throw; }
        });
    }
    private Task OpenMonitorAgreementAsync(Guid? agreementId) => RunAsync(async token =>
    {
        if (!agreementId.HasValue)
            return;
        var result = Require(await _client.GetAsync(agreementId.Value, Date(AsOf, "As-of date"), token));
        token.ThrowIfCancellationRequested();
        Apply(result);
        StatusText = "Legal revision and review history loaded from the selected advisory finding or guarantee disclosure.";
    });
    private void ResetObligationMonitor()
    { ResetLegacyCoverage(); SelectedGuaranteeDisclosure = null; SelectedObligationFinding = null; ObligationMonitor = null; ObligationFindings.Clear(); GuaranteeDisclosures.Clear(); ObligationBalances.Clear(); ObligationStatus = "Not evaluated for the selected book/date. Findings never block close."; }
    private static string StructuredSummary(IntercompanyStructuredTermsDto? terms)
        => terms is null ? "Terms format: Unstructured" : $"Terms format: StructuredV1 · schema {terms.SchemaVersion}\n"
            + $"Rate {terms.RateBasis} · fixed annual {Value(terms.FixedAnnualRate)} · {terms.FloatingIndexReference} + {Value(terms.SpreadBps)} bps · {terms.DayCount} · {terms.Compounding}\n"
            + $"Schedule {terms.Schedule?.StartDate:yyyy-MM-dd}–{terms.Schedule?.EndDate:yyyy-MM-dd} · {terms.Schedule?.Frequency} · {terms.Schedule?.CalendarId} · {terms.Schedule?.BusinessDayConvention} · {terms.Schedule?.SettlementBusinessDays} business days\n"
            + $"Fee {Value(terms.FixedFeeAmount)} or annual {Value(terms.AnnualFeeRate)} · base {terms.CalculationBaseReference}\n"
            + $"Allocation driver {terms.AllocationDriverReference} · {string.Join(", ", (terms.Allocations ?? []).Select(x => $"{x.EntityId}: {Value(x.Percent)}%"))}\n"
            + $"Guarantee beneficiary {terms.Guarantee?.BeneficiaryReference} · declared limit {Value(terms.Guarantee?.LimitAmount)}";
    private void NotifyStructuredCommands()
    {
        OpenGuaranteeDisclosureCommand?.NotifyCanExecuteChanged();
        InspectObligationAgreementCommand?.NotifyCanExecuteChanged();
        NotifyLegacyCommands();
        AddStructuredAllocationCommand?.NotifyCanExecuteChanged();
        RemoveStructuredAllocationCommand?.NotifyCanExecuteChanged();
        PreviewProposalDatesCommand?.NotifyCanExecuteChanged();
        PreviewRevisionDatesCommand?.NotifyCanExecuteChanged();
        EvaluateObligationsCommand?.NotifyCanExecuteChanged();
    }
}

public sealed class IntercompanyAllocationEditor : ObservableObject
{
    private string _entityId = "", _percent = "";
    public string EntityId { get => _entityId; set => SetProperty(ref _entityId, value); }
    public string Percent { get => _percent; set => SetProperty(ref _percent, value); }
}
