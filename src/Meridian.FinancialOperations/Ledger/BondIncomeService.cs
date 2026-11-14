using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Prepares canonical, non-posting income instructions from actual book/lot authorities.
/// Caller amounts, policy choices, fixing values and journal lines are never accepted.
/// </summary>
public sealed class BondIncomeService(ILedgerJournalStore lots, IInstrumentPositionProjectionStore positions,
    ISecurityMasterStore securities, IAccountingPolicyService policies, IBondWorkflowService workflow,
    IBondIncomePreparationEvidenceStore? preparationEvidence = null) : IBondIncomeService
{
    public async Task VerifyPreparedSourceAsync(OpenLotAmortizationInstructionDto instruction,
        EconomicEventReferenceDto economicEvent, Guid periodId, IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        CancellationToken ct = default)
    {
        var source = evidence.SingleOrDefault(e => e.SubjectType == AssetAccountingEvidenceSubjects.Event
            && e.SubjectId == economicEvent.EventId.ToString("D") && e.SourceSystem == "Meridian.BondIncome"
            && e.SourceSystem == economicEvent.SourceDomain && e.EvidenceVersion == economicEvent.EventVersion
            && e.ContentHashSha256 == economicEvent.SourceContentHash && e.SourceReference == economicEvent.SourceEntityId)
            ?? throw new InvalidOperationException("The canonical income event requires its actual operator-reviewed retained calculation source.");
        await (preparationEvidence ?? throw new InvalidOperationException("Income preparation evidence authority is unavailable."))
            .VerifyAsync(source, instruction, periodId, economicEvent.EventId, economicEvent.EventVersion, ct).ConfigureAwait(false);
    }

    public async Task RequireAcquisitionPolicyAsync(Guid ledgerBookId, DateOnly acquiredDate,
        BondIncomeAcquisitionDto acquisition, decimal retainedCost, CancellationToken ct = default)
    {
        BondIncomeCalculator.ValidateAcquisition(acquisition, retainedCost);
        BondIncomeBookedPrecision.RequireRepresentable(retainedCost);
        BondIncomeBookedPrecision.RequireRepresentable(acquisition.CleanConsideration);
        BondIncomeBookedPrecision.RequireRepresentable(acquisition.PurchasedAccruedInterest);
        foreach (var fee in acquisition.Fees)
            BondIncomeBookedPrecision.RequireRepresentable(fee.Amount);
        var book = await lots.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative acquisition book is unavailable.");
        var adopted = BondIncomeCalculator.ReadPolicy(await policies.ResolvePolicyAsync(new(book.AccountingBasis,
            acquiredDate, book.AccountingPolicyId, book.FundProfileId, book.FundStructureNodeId,
            PolicyVersion: book.AccountingPolicyVersion), ct).ConfigureAwait(false));
        if (acquisition.AccrualPolicyVersion != adopted.AccrualPolicyVersion)
            throw new InvalidOperationException("The acquisition must capture the authoritative book's explicitly adopted coupon-accrual policy.");
        if (!adopted.CapitalizeEligibleDirectCosts && acquisition.Fees.Any(f =>
            f.Treatment == BondIncomeFeeTreatment.EligibleDirectAcquisitionCost && f.Amount != 0m))
            throw new InvalidOperationException("The authoritative book policy has not adopted direct-cost capitalization.");
    }

    public async Task<BondIncomePreviewDto> PreviewAsync(BondIncomePreviewRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LedgerBookId == Guid.Empty || request.TaxLotRecordId == Guid.Empty || request.AccountId == Guid.Empty || request.ThroughDate == default
            || request.KnownAt == default || request.KnownAt.Offset != TimeSpan.Zero
            || request.ThroughDate > DateOnly.FromDateTime(request.KnownAt.UtcDateTime))
            throw new ArgumentException("Bond income requires exact book/lot, elapsed recognition date and explicit UTC cutoff.");
        var book = await lots.GetLedgerBookAsync(request.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative ledger book is unavailable.");
        var retained = (await lots.GetTaxLotsByIdsAsync(request.LedgerBookId, [request.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The authoritative lot is unavailable in the requested book.");
        var lot = retained.ToOpenLot();
        var acquisition = lot.Acquisition.BondIncome
            ?? throw new InvalidOperationException("The lot requires reviewed bond-income acquisition cost, fee and index evidence.");
        BondIncomeCalculator.ValidateAcquisition(acquisition, lot.Acquisition.TransactionCostBasis);
        if (lot.LedgerBookId != book.LedgerBookId || lot.Acquisition.Direction != OpenLotDirection.Long
            || retained.Account.AccountType != Meridian.Ledger.LedgerAccountType.Asset
            || lot.Acquisition.FunctionalCurrency != book.BaseCurrency
            || request.ThroughDate < lot.AcquiredDate || request.KnownAt < acquisition.KnownAt)
            throw new InvalidOperationException("Bond income requires an eligible performing long asset lot in this book/date scope.");
        if (!Guid.TryParseExact(retained.Account.FinancialAccountId, "D", out var financialAccountId)
            || financialAccountId != request.AccountId
            || book.FundStructureNodeKind == Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Account
                && book.FundStructureNodeId != request.AccountId)
            throw new InvalidOperationException("The authoritative lot does not belong to the requested financial account.");
        var position = await positions.GetBookPositionAsync(lot.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The authoritative book position is unavailable.");
        if (position.BookContext.LedgerBookId != book.LedgerBookId || position.SecurityId != lot.SecurityId
            || position.BookContext.FundProfileId != book.FundProfileId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.BookContext.BaseCurrency != book.BaseCurrency
            || position.BookContext.AccountingBasis != book.AccountingBasis
            || position.Status != "Active" || position.BookContext.AccountingPolicyId != book.AccountingPolicyId
            || position.BookContext.AccountingPolicyVersion != book.AccountingPolicyVersion
            || position.EffectiveFrom > request.ThroughDate || position.EffectiveTo is { } end && end < request.ThroughDate)
            throw new InvalidOperationException("The lot's authoritative position or book policy is missing or stale.");
        var policyRecord = await policies.ResolvePolicyAsync(new(book.AccountingBasis, request.ThroughDate,
            book.AccountingPolicyId, book.FundProfileId, book.FundStructureNodeId,
            PolicyVersion: book.AccountingPolicyVersion), ct).ConfigureAwait(false);
        var policy = BondIncomeCalculator.ReadPolicy(policyRecord);
        if (policy.AccrualPolicyVersion != acquisition.AccrualPolicyVersion)
            throw new InvalidOperationException("The immutable acquisition has not adopted this book accrual-policy version; reconcile and review the opening before changing policy.");
        var separatedAccrual = policy.AccrualPolicyVersion == BondIncomeCalculator.SeparatedAccrualPolicyVersion;
        if (!separatedAccrual && request.ThroughDate == lot.AcquiredDate)
            throw new InvalidOperationException("Legacy income preparation requires a recognition date after acquisition.");
        var workflowModel = separatedAccrual ? BondIncomeCalculator.SeparatedAccrualWorkflowModelVersion : BondWorkflowModelVersions.Legacy;
        var security = await securities.GetProjectionAsync(lot.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Versioned Security Master terms are unavailable.");
        var acceptedSecurityHashes = OpenLotAmortization.AcceptedSecurityHashes(security);
        var securityEvidence = acquisition.Evidence.LastOrDefault(e => e.SubjectType == "SecurityMasterProjection"
            && e.SubjectId == lot.SecurityId.ToString("D") && e.EvidenceVersion == security.Version
            && acceptedSecurityHashes.Any(hash => Sha256Digest.FixedEquals(e.ContentHashSha256, hash)))
            ?? throw new InvalidOperationException("The acquisition requires reviewed retained evidence matching the current Security Master terms.");
        var reference = StructuredCashFlowTermsResolver.Resolve(security.ToDetail());
        var zeroCoupon = reference.CouponKind == SecurityCouponKind.ZeroCoupon;
        var frequency = reference.PaymentFrequency?.Trim().ToLowerInvariant() switch
        {
            "atmaturity" or "at-maturity" when zeroCoupon => 1,
            "annual" or "annually" or "1" => 1,
            "semiannual" or "semi-annual" or "semiannually" or "2" => 2,
            "quarterly" or "4" => 4,
            "monthly" or "12" => 12,
            _ => throw new InvalidOperationException("Accounting income requires the explicit contractual coupon frequency.")
        };
        var conventions = SecurityVersionedTerms.ReadDeclaredScheduleConventions(security.AssetClass, security.AssetSpecificTerms);
        var face = lot.Acquisition.FaceValueTerms
            ?? throw new InvalidOperationException("The authoritative acquisition has no face/factor units.");
        var originalFace = lot.OriginalQuantity / face.BookedFactor;
        BondWorkflowRequest ScheduleRequest(DateOnly asOf, DateOnly settle, DateTimeOffset cutoff) => new(lot.SecurityId,
            new(new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), settle, cutoff, security.Version), originalFace)
        { ModelVersion = workflowModel };
        async Task<BondWorkflowResult> Frozen(DateOnly asOf, DateOnly settle, DateTimeOffset cutoff, DateOnly freeze)
        {
            var value = await workflow.CalculateIncomeScheduleAsync(ScheduleRequest(asOf, settle, cutoff), freeze, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The retained bond-income projection is unavailable.");
            if (value.IsBlocked)
                throw new InvalidOperationException(string.Join(" ", value.Issues.Where(i => i.BlocksCalculation).Select(i => i.Code + ": " + i.Reason)));
            return value;
        }
        var acquired = await Frozen(lot.AcquiredDate, lot.AcquiredDate, acquisition.KnownAt, lot.AcquiredDate).ConfigureAwait(false);
        var current = await Frozen(request.ThroughDate, lot.AcquiredDate, request.KnownAt, request.ThroughDate).ConfigureAwait(false);
        var currentAtDate = await Frozen(request.ThroughDate, request.ThroughDate, request.KnownAt, request.ThroughDate).ConfigureAwait(false);
        if (separatedAccrual && (acquired.HasPaidActiveCoupon == true || currentAtDate.HasPaidActiveCoupon == true))
            throw new InvalidOperationException("Early-paid coupons with an unearned accrual tail require reviewed advance-payment accounting before book-income preparation.");
        if (!separatedAccrual && current.CashFlows.Any(flow => flow.Interest != 0m && flow.AccrualEnd <= request.ThroughDate
                && flow.PaymentDate > request.ThroughDate)
            && current.CashFlows.Any(flow => flow.AccrualStart <= request.ThroughDate && request.ThroughDate < flow.AccrualEnd))
            throw new InvalidOperationException("Legacy coupon accrual cannot prepare a multi-period payment-lag window; review bond-income-accrual-v2 policy and acquisition opening authority.");
        var hasExCoupon = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(security.AssetSpecificTerms) >= 5
            && security.AssetSpecificTerms.TryGetProperty("bondWorkflow", out var workflowTerms)
            && SecurityTermReader.ReadDecimal(workflowTerms, "exCouponBusinessDays") is > 0m;
        if (hasExCoupon && (acquired.ContractualAccruedInterest is null || currentAtDate.ContractualAccruedInterest is null))
            throw new InvalidOperationException("Held ex-coupon accounting requires retained contractual accrual distinct from purchaser invoice accrual.");
        if (hasExCoupon && separatedAccrual && acquired.HasDetachedCoupon is null)
            throw new InvalidOperationException("Separated ex-coupon accounting requires explicit retained per-obligation detachment evidence.");
        if (hasExCoupon && (separatedAccrual ? acquired.HasDetachedCoupon == true : acquired.ContractualAccruedInterest != acquired.AccruedInterest))
            throw new InvalidOperationException("An acquisition inside its ex-coupon window requires retained opening entitlement reconciliation before accounting-income adoption.");
        await BondIncomeEntitlementHistoryGuard.RequireAsync(lots, security, current, lot, request.AccountId,
            request.ThroughDate, ct).ConfigureAwait(false);
        var cashAllocations = new List<BondIncomeCouponAllocationDto>();
        var lastPrincipal = current.CashFlows.Where(f => f.Principal > 0m).OrderBy(f => f.PaymentDate).LastOrDefault();
        var terminalEntitlement = lastPrincipal is { OutstandingPrincipal: 0m } && lastPrincipal.PaymentDate <= request.ThroughDate
            ? new BondIncomeTerminalEntitlementDto(BondIncomeCouponRounding.TerminalEntitlementModelVersion, lastPrincipal.PaymentDate) : null;
        if (terminalEntitlement is not null && current.CashFlows.Any(f => f.Interest != 0m && f.PaymentDate > terminalEntitlement.PrincipalDate
            && (f.IsForecast || f.AccrualStart >= terminalEntitlement.PrincipalDate)))
            throw new InvalidOperationException("Terminal coupon rights must be fully earned and observed before closing; future unearned or unfixed coupons require retained entitlement authority.");
        var holderEntitlements = new Dictionary<Guid, BondWorkflowResult> { [retained.TaxLotRecordId] = current };
        foreach (var obligation in current.CashFlows.Where(f => f.Interest != 0m && f.PaymentDate > lot.AcquiredDate
            && (f.PaymentDate <= request.ThroughDate || terminalEntitlement is not null)).GroupBy(f => (f.PaymentDate, f.AccrualStart, f.AccrualEnd))
            .OrderBy(g => g.Key.PaymentDate).ThenBy(g => g.Key.AccrualStart))
        {
            var holderDate = terminalEntitlement is not null && obligation.Key.PaymentDate > terminalEntitlement.PrincipalDate
                ? terminalEntitlement.PrincipalDate : obligation.Key.PaymentDate;
            var holderLots = (await lots.ListOpenTaxLotsByAssetScopeAsync(book.LedgerBookId, lot.SecurityId,
                lot.BookPositionId, holderDate, ct).ConfigureAwait(false))
                .Where(l => Guid.TryParse(l.Account.FinancialAccountId, out var owner) && owner == request.AccountId).ToArray();
            if (!holderLots.Any(l => l.TaxLotRecordId == lot.TaxLotRecordId)
                || holderLots.Any(l => !l.HasFaceValueTerms || l.BookedFactor is not > 0m || l.OpenQuantity <= 0m
                    || l.Account.AccountType != Meridian.Ledger.LedgerAccountType.Asset || l.Currency != retained.Currency))
                throw new InvalidOperationException("Completed coupon cash requires complete authoritative historical holder lots and face units.");
            var components = new List<BondIncomeCouponLotAllocationDto>();
            foreach (var holderLot in holderLots)
            {
                if (!holderEntitlements.TryGetValue(holderLot.TaxLotRecordId, out var entitlement))
                {
                    var parFace = holderLot.OriginalQuantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis / holderLot.BookedFactor!.Value;
                    var peerRequest = new BondWorkflowRequest(lot.SecurityId,
                        new(new DateTimeOffset(request.ThroughDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                            holderLot.AcquiredDate, request.KnownAt, security.Version), parFace)
                    { ModelVersion = workflowModel };
                    entitlement = await workflow.CalculateIncomeScheduleAsync(peerRequest, request.ThroughDate, ct).ConfigureAwait(false);
                    if (entitlement is null || entitlement.IsBlocked || entitlement.Inputs.Count == 0
                        || entitlement.SecurityId != lot.SecurityId || entitlement.Currency != security.Currency
                        || entitlement.OriginalFace != parFace || entitlement.ModelVersion != peerRequest.ModelVersion
                        || entitlement.Context.AsOf != peerRequest.Context.AsOf
                        || entitlement.Context.KnownAt != peerRequest.Context.KnownAt
                        || entitlement.Context.SettlementDate != peerRequest.Context.SettlementDate
                        || entitlement.Context.SecurityVersion != peerRequest.Context.SecurityVersion
                        || JsonSerializer.Serialize(entitlement.Context.ObservedFixingReceiptIds)
                            != JsonSerializer.Serialize(peerRequest.Context.ObservedFixingReceiptIds))
                        throw new InvalidOperationException("Coupon cash allocation requires retained acquisition-settlement entitlement for every holder lot.");
                    await RequirePeerCurveAuthorityAsync(peerRequest, entitlement, request.ThroughDate, ct).ConfigureAwait(false);
                    holderEntitlements.Add(holderLot.TaxLotRecordId, entitlement);
                }
                var heldPar = holderLot.OpenQuantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis / holderLot.BookedFactor!.Value;
                var peerCash = entitlement.CashFlows.Where(f => f.PaymentDate == obligation.Key.PaymentDate
                    && f.AccrualStart == obligation.Key.AccrualStart && f.AccrualEnd == obligation.Key.AccrualEnd).ToArray();
                if (peerCash.Any(f => f.IsForecast))
                    throw new InvalidOperationException("Completed holder coupon cash requires observed peer entitlement inputs.");
                var raw = peerCash.Sum(f => f.Interest) * holderLot.OpenQuantity / holderLot.OriginalQuantity;
                components.Add(new(holderLot.TaxLotRecordId, holderLot.Version, heldPar, raw, 0m)
                { AcquisitionSettlementDate = holderLot.AcquiredDate, EntitlementInputs = entitlement.Inputs });
            }
            cashAllocations.Add(BondIncomeCouponRounding.Allocate(obligation.Key.PaymentDate, obligation.Key.AccrualStart,
                obligation.Key.AccrualEnd, security.Currency, components) with
            { HolderSnapshotDate = holderDate });
        }
        var resets = new List<BondIncomeResetDto>();
        if (policy.FloaterPolicy == BondIncomeFloaterPolicy.ProspectiveReset)
        {
            decimal? previousRate = null;
            foreach (var observation in current.CashFlows.SelectMany(f => f.Observations)
                .Where(o => o.IsObserved && o.AccrualDate > lot.AcquiredDate && o.AccrualDate <= request.ThroughDate)
                .GroupBy(o => o.AccrualDate).Select(g => g.First()).OrderBy(o => o.AccrualDate))
            {
                if (previousRate == observation.Rate)
                    continue;
                if (resets.Count >= 1200)
                    throw new InvalidOperationException("Income reset history exceeds the bounded retained-schedule limit.");
                resets.Add(new(observation.AccrualDate, await Frozen(observation.AccrualDate, observation.AccrualDate,
                    request.KnownAt, observation.AccrualDate).ConfigureAwait(false)));
                previousRate = observation.Rate;
            }
        }
        var freezeAt = resets.LastOrDefault()?.Date ?? lot.AcquiredDate;
        var frozenCutoff = policy.FloaterPolicy == BondIncomeFloaterPolicy.AcquisitionIndex ? acquisition.KnownAt : request.KnownAt;
        var amortization = await Frozen(request.ThroughDate, request.ThroughDate, frozenCutoff, freezeAt).ConfigureAwait(false);
        if (currentAtDate.SettlementIndexRatio is not { } indexRatio || currentAtDate.SettlementIndexIsObserved != true)
            throw new InvalidOperationException("Recognized indexation requires observed contractual settlement-index evidence.");
        var incomeInputs = new BondIncomeInstructionDto(book.LedgerBookId, book.AccountingPolicyId, book.AccountingPolicyVersion,
            policy, lot.AcquiredDate, request.ThroughDate, request.KnownAt, acquired, current, amortization, resets,
            BondIncomeCalculator.ReadQualifyingCalls(security), acquisition.Fees, acquisition.PurchasedAccruedInterest,
            currentAtDate.ContractualAccruedInterest ?? currentAtDate.AccruedInterest
                ?? throw new InvalidOperationException("Observed contractual accrued interest is unavailable."),
            acquisition.IndexRatio, indexRatio, originalFace * (lot.CurrentFactor ?? face.BookedFactor), acquisition.Evidence, request.AccountId,
            frequency, conventions.Block.CouponCashBasis == "Level", DayCountConventions.Parse(reference.DayCountConvention), zeroCoupon)
        { CouponCashAllocations = cashAllocations, TerminalEntitlement = terminalEntitlement };
        // A coupon already wholly earned by the seller is separately booked purchased interest.
        // Admit only the exact par, one-obligation tail; actual birth/account/history authority is
        // revalidated by the terminal opening companion before principal can close the lot.
        var purchasedCoupons = acquired.CashFlows.Where(f => f.Interest != 0m && f.PaymentDate > lot.AcquiredDate).ToArray();
        if (!zeroCoupon && terminalEntitlement is not null && purchasedCoupons.Length == 1
            && purchasedCoupons[0].AccrualEnd <= lot.AcquiredDate && acquisition.PurchasedAccruedInterest != 0m)
        {
            var coupon = purchasedCoupons[0];
            if (retained.OriginatingMutationBatchId is not { } birthId || retained.SourceJournalEntryId is not { } birthJournalId)
                throw new InvalidOperationException("Purchased coupon separation requires its actual canonical acquisition receipt and journal.");
            var birth = await lots.GetAtomicTaxLotPostingAsync(birthId, ct).ConfigureAwait(false);
            if (birth is null || birth.MutationKind != AtomicTaxLotMutationKind.Acquisition || birth.Mutations.Count != 1
                || birth.Journal.Entry.JournalEntryId != birthJournalId || birth.Journal.AggregateId != book.LedgerBookId
                || birth.Mutations[0].LotBefore is not null || birth.Mutations[0].TaxLotRecordId != lot.TaxLotRecordId
                || birth.Mutations[0].LotAfter.SourceJournalEntryId != birthJournalId
                || birth.Mutations[0].LotAfter.OriginatingMutationBatchId != birthId
                || JsonSerializer.Serialize(birth.Mutations[0].LotAfter.Acquisition) != JsonSerializer.Serialize(lot.Acquisition)
                || birth.Journal.CreatedAt > request.KnownAt || birth.Mutations[0].RecordedAt > request.KnownAt)
                throw new InvalidOperationException("Purchased coupon separation requires exact retained canonical acquisition authority at the calculation cutoff.");
            incomeInputs = incomeInputs with
            {
                PurchasedCouponTail = new(BondIncomePurchasedCouponTail.ModelVersion,
                birthId, birthJournalId, coupon.PaymentDate, coupon.AccrualStart, coupon.AccrualEnd, acquisition.PurchasedAccruedInterest)
            };
        }
        var income = BondIncomeCalculator.Project(lot, incomeInputs);
        incomeInputs = incomeInputs with { BookedTarget = income.BookedTarget, AccountingYieldCertificate = income.AccountingYieldCertificate };
        var instruction = new OpenLotAmortizationInstructionDto(lot, security, securityEvidence, position.Version,
            request.ThroughDate, BondIncomeCalculator.ModelVersion)
        { BondIncome = incomeInputs };
        _ = OpenLotAmortization.Project(instruction);
        var mutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Amortize,
            AssetAccountId: position.PrimaryAccountId
                ?? throw new InvalidOperationException("The governed book position has no carrying-account mapping."), Amortization: instruction);
        var fingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(instruction));
        return new(book.LedgerBookId, lot.TaxLotRecordId, fingerprint, instruction, income, mutation);
    }

    private async Task RequirePeerCurveAuthorityAsync(BondWorkflowRequest requested, BondWorkflowResult entitlement,
        DateOnly freezeAt, CancellationToken ct)
    {
        var pins = entitlement.Context.Curves ?? [];
        var dependencies = entitlement.Inputs.Where(input => input.Kind.StartsWith("Curve:", StringComparison.Ordinal)).ToArray();
        if (pins.Count == 0 && dependencies.Length == 0)
            return;
        if (pins.Count != dependencies.Length || pins.GroupBy(pin => pin.Role, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || dependencies.GroupBy(input => input.Kind, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || entitlement.Request is not { } retainedRequest || entitlement.FrozenIndexAt != freezeAt
            || JsonSerializer.Serialize(retainedRequest.Context) != JsonSerializer.Serialize(entitlement.Context)
            || JsonSerializer.Serialize(retainedRequest with { Context = requested.Context }) != JsonSerializer.Serialize(requested))
            throw new InvalidOperationException("Peer coupon entitlement requires exact request and frozen-policy identity with retained resolved curve dependencies.");
        foreach (var pin in pins)
        {
            var input = dependencies.SingleOrDefault(item => item.Kind == "Curve:" + pin.Role);
            if (pin.CurveId == Guid.Empty || pin.CurveVersionId == Guid.Empty || input is null
                || input.VersionId != pin.CurveVersionId || input.Version is not > 0
                || !Sha256Digest.IsCanonical(input.ContentSha256) || string.IsNullOrWhiteSpace(input.Key)
                || input.AsOf is not { } economic || economic.Offset != TimeSpan.Zero
                || input.RecordedAt is not { } recorded || recorded.Offset != TimeSpan.Zero
                || economic > requested.Context.AsOf || recorded > requested.Context.KnownAt)
                throw new InvalidOperationException("Peer coupon resolved curve evidence differs from its exact identity or economic/knowledge cutoff.");
        }
        // Replay resolves each retained role against its actual curve definition and immutable
        // version, including definition admission time, currency/index, hash and both cutoffs.
        BondWorkflowResult? replay;
        try
        { replay = await workflow.ReplayAsync(entitlement, ct).ConfigureAwait(false); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new InvalidOperationException("Peer coupon resolved curves require authoritative retained-input replay.", error); }
        if (replay is null || JsonSerializer.Serialize(replay) != JsonSerializer.Serialize(entitlement))
            throw new InvalidOperationException("Peer coupon resolved curves or entitlement changed during authoritative replay.");
    }

    public async Task<BondIncomePreviewDto> PrepareAsync(BondIncomePreparationRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await PreviewAsync(request.Request, ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(current.Fingerprint, request.ExpectedFingerprint))
            throw new InvalidOperationException("Bond income review is stale: book, policy, lot, terms or retained inputs changed. Rebuild and review the preview.");
        return current;
    }
}
