using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLendingInterop;

namespace Meridian.Application.DirectLending;

public sealed partial class InMemoryDirectLendingService : IDirectLendingService, IBankTransactionSeedService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredLoan> _loans = new();

    public Task<LoanContractDetailDto> CreateLoanAsync(CreateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateRequest(request);

        var loanId = request.LoanId.GetValueOrDefault(Guid.NewGuid());
        var recordedAt = DateTimeOffset.UtcNow;
        var termsVersion = CreateTermsVersion(1, request.Terms, "LoanCreated", amendmentReason: null, recordedAt);
        var currentCommitment = request.Terms.CommitmentAmount;

        var stored = new StoredLoan(
            loanId,
            request.FacilityName.Trim(),
            request.Borrower,
            request.EffectiveDate,
            LoanStatus.Draft,
            activationDate: null,
            closeDate: null,
            [termsVersion],
            new ServicingState(
                LoanStatus.Draft,
                currentCommitment,
                TotalDrawn: 0m,
                AvailableToDraw: DirectLendingInterop.CalculateAvailableToDraw(currentCommitment, 0m),
                new OutstandingBalancesDto(0m, 0m, 0m, 0m, 0m),
                new List<DrawdownLotDto>(),
                CurrentRateReset: null,
                LastAccrualDate: null,
                LastPaymentDate: null,
                ServicingRevision: 0,
                new List<ServicingRevisionDto>(),
                new List<DailyAccrualEntryDto>()),
            aggregateVersion: 0,
            history: new List<LoanEventLineageDto>());

        AppendEvent(stored, "loan.created", request.EffectiveDate, new
        {
            loanId,
            request.FacilityName,
            request.Borrower,
            request.EffectiveDate,
            request.Terms
        }, metadata);

        lock (_gate)
        {
            if (_loans.ContainsKey(loanId))
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, $"Loan '{loanId}' already exists."));
            }

            _loans.Add(loanId, stored);
            return Task.FromResult(ToContractDetail(stored));
        }
    }

    public Task<LoanContractDetailDto?> GetLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_loans.TryGetValue(loanId, out var stored) ? ToContractDetail(stored) : null);
        }
    }

    public Task<LoanContractDetailDto?> GetContractProjectionAsync(Guid loanId, CancellationToken ct = default)
        => GetLoanAsync(loanId, ct);

    public Task<LoanAggregateSnapshotDto?> RebuildStateFromHistoryAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<LoanAggregateSnapshotDto?>(
                _loans.TryGetValue(loanId, out var stored)
                    ? new LoanAggregateSnapshotDto(
                        loanId,
                        stored.AggregateVersion,
                        ToContractDetail(stored),
                        ToServicingState(stored))
                    : null);
        }
    }

    public Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LoanEventLineageDto>>(
                _loans.TryGetValue(loanId, out var stored)
                    ? stored.History.OrderBy(static item => item.AggregateVersion).ToArray()
                    : []);
        }
    }

    public Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionsAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LoanTermsVersionDto>>(
                _loans.TryGetValue(loanId, out var stored)
                    ? stored.TermsVersions.OrderByDescending(static item => item.VersionNumber).ToArray()
                    : []);
        }
    }

    public Task<IReadOnlyList<LoanTermsVersionDto>> GetTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default)
        => GetTermsVersionsAsync(loanId, ct);

    public Task<LoanContractDetailDto?> AmendTermsAsync(Guid loanId, AmendLoanTermsRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTerms(request.Terms);

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanContractDetailDto?>(null);
            }

            var nextVersion = stored.TermsVersions[^1].VersionNumber + 1;
            var termsVersion = CreateTermsVersion(nextVersion, request.Terms, "TermsAmended", request.AmendmentReason, DateTimeOffset.UtcNow);
            stored.TermsVersions.Add(termsVersion);

            var servicing = stored.Servicing;
            var updatedCommitment = request.Terms.CommitmentAmount;
            stored.Servicing = servicing with
            {
                CurrentCommitment = updatedCommitment,
                AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(updatedCommitment, servicing.TotalDrawn)
            };
            AppendEvent(stored, "loan.terms-amended", DateOnly.FromDateTime(DateTime.UtcNow), new
            {
                loanId,
                request.AmendmentReason,
                request.Terms,
                versionNumber = nextVersion
            }, metadata);

            return Task.FromResult<LoanContractDetailDto?>(ToContractDetail(stored));
        }
    }

    public Task<LoanContractDetailDto?> ActivateLoanAsync(Guid loanId, ActivateLoanRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanContractDetailDto?>(null);
            }

            stored.Status = LoanStatus.Active;
            stored.ActivationDate = request.ActivationDate;
            stored.Servicing = stored.Servicing with
            {
                Status = LoanStatus.Active
            };
            AppendEvent(stored, "loan.activated", request.ActivationDate, new
            {
                loanId,
                request.ActivationDate
            }, metadata);

            return Task.FromResult<LoanContractDetailDto?>(ToContractDetail(stored));
        }
    }

    public Task<LoanServicingStateDto?> GetServicingStateAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_loans.TryGetValue(loanId, out var stored) ? ToServicingState(stored) : null);
        }
    }

    public Task<LoanServicingStateDto?> GetServicingProjectionAsync(Guid loanId, CancellationToken ct = default)
        => GetServicingStateAsync(loanId, ct);

    public Task<IReadOnlyList<DrawdownLotDto>> GetDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DrawdownLotDto>>(
                _loans.TryGetValue(loanId, out var stored)
                    ? stored.Servicing.DrawdownLots.OrderBy(static item => item.DrawdownDate).ToArray()
                    : []);
        }
    }

    public Task<IReadOnlyList<ServicingRevisionDto>> GetServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ServicingRevisionDto>>(
                _loans.TryGetValue(loanId, out var stored)
                    ? stored.Servicing.RevisionHistory.OrderByDescending(static item => item.RevisionNumber).ToArray()
                    : []);
        }
    }

    public Task<IReadOnlyList<DailyAccrualEntryDto>> GetAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DailyAccrualEntryDto>>(
                _loans.TryGetValue(loanId, out var stored)
                    ? stored.Servicing.AccrualEntries.OrderByDescending(static item => item.AccrualDate).ToArray()
                    : []);
        }
    }

    public Task<LoanServicingStateDto?> BookDrawdownAsync(Guid loanId, BookDrawdownRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Drawdown amount must be positive."));
        }

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            EnsureActive(stored);

            if (request.Amount > stored.Servicing.AvailableToDraw)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Drawdown exceeds available commitment."));
            }

            var lot = new DrawdownLotDto(
                Guid.NewGuid(),
                request.TradeDate,
                request.SettleDate,
                request.Amount,
                request.Amount,
                request.ExternalRef);

            stored.Servicing.DrawdownLots.Add(lot);
            var balances = stored.Servicing.Balances with
            {
                PrincipalOutstanding = stored.Servicing.Balances.PrincipalOutstanding + request.Amount
            };

            stored.Servicing = stored.Servicing with
            {
                TotalDrawn = stored.Servicing.TotalDrawn + request.Amount,
                AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(
                    stored.Servicing.CurrentCommitment,
                    stored.Servicing.TotalDrawn + request.Amount),
                Balances = balances
            };

            AppendRevision(stored, "InternalEvent", request.SettleDate, $"Drawdown booked for {request.Amount:0.00}.");
            AppendEvent(stored, "loan.drawdown-booked", request.SettleDate, new
            {
                loanId,
                lot.LotId,
                request.Amount,
                request.TradeDate,
                request.SettleDate,
                request.ExternalRef
            }, metadata);
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public Task<LoanServicingStateDto?> ApplyRateResetAsync(Guid loanId, ApplyRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            var currentTerms = stored.TermsVersions[^1].Terms;
            if (currentTerms.RateTypeKind != RateTypeKind.Floating)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Rate resets can only be applied to floating-rate loans."));
            }

            var spreadBps = request.SpreadBps ?? currentTerms.SpreadBps ?? 0m;
            var allInRate = DirectLendingInterop.CalculateAllInRate(
                request.ObservedRate,
                spreadBps,
                ToNullable(currentTerms.FloorRate),
                ToNullable(currentTerms.CapRate));

            stored.Servicing = stored.Servicing with
            {
                CurrentRateReset = new RateResetDto(
                    request.EffectiveDate,
                    currentTerms.InterestIndexName ?? "FloatingIndex",
                    request.ObservedRate,
                    spreadBps,
                    allInRate,
                    request.SourceRef)
            };

            AppendRevision(stored, "InternalEvent", request.EffectiveDate, $"Rate reset applied at {allInRate:P4}.");
            AppendEvent(stored, "loan.rate-reset-applied", request.EffectiveDate, new
            {
                loanId,
                request.EffectiveDate,
                request.ObservedRate,
                SpreadBps = spreadBps,
                AllInRate = allInRate,
                request.SourceRef
            }, metadata);
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public Task<LoanServicingStateDto?> ApplyPrincipalPaymentAsync(Guid loanId, ApplyPrincipalPaymentRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Principal payment amount must be positive."));
        }

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            var principalBefore = stored.Servicing.Balances.PrincipalOutstanding;
            if (principalBefore <= 0m)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "No principal is outstanding."));
            }

            var appliedAmount = Math.Min(request.Amount, principalBefore);
            var principalAfter = DirectLendingInterop.ApplyPrincipalPayment(principalBefore, appliedAmount);
            var balances = stored.Servicing.Balances with
            {
                PrincipalOutstanding = principalAfter
            };

            ApplyPaymentToLots(stored.Servicing.DrawdownLots, appliedAmount);

            var totalDrawnAfter = Math.Max(0m, stored.Servicing.TotalDrawn - appliedAmount);
            stored.Servicing = stored.Servicing with
            {
                TotalDrawn = totalDrawnAfter,
                AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(stored.Servicing.CurrentCommitment, totalDrawnAfter),
                Balances = balances,
                LastPaymentDate = request.EffectiveDate
            };

            AppendRevision(stored, "InternalEvent", request.EffectiveDate, $"Principal payment applied for {appliedAmount:0.00}.");
            AppendEvent(stored, "loan.principal-payment-applied", request.EffectiveDate, new
            {
                loanId,
                RequestedAmount = request.Amount,
                AppliedAmount = appliedAmount,
                request.EffectiveDate,
                request.ExternalRef
            }, metadata);
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public Task<DailyAccrualEntryDto?> PostDailyAccrualAsync(Guid loanId, PostDailyAccrualRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<DailyAccrualEntryDto?>(null);
            }

            EnsureActive(stored);

            if (stored.Servicing.LastAccrualDate is not null && request.AccrualDate <= stored.Servicing.LastAccrualDate.Value)
            {
                throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Accrual date must be greater than the last accrual date."));
            }

            var terms = stored.TermsVersions[^1].Terms;
            var annualRate = ResolveAnnualRate(terms, stored.Servicing.CurrentRateReset);
            var interestAmount = DirectLendingInterop.CalculateDailyAccrualAmount(
                stored.Servicing.Balances.PrincipalOutstanding,
                annualRate,
                (int)terms.DayCountBasis);

            var commitmentFeeAmount = terms.CommitmentFeeRate is { } feeRate && feeRate > 0m
                ? DirectLendingInterop.CalculateDailyAccrualAmount(
                    stored.Servicing.AvailableToDraw,
                    feeRate,
                    (int)terms.DayCountBasis)
                : 0m;

            var entry = new DailyAccrualEntryDto(
                Guid.NewGuid(),
                request.AccrualDate,
                interestAmount,
                commitmentFeeAmount,
                0m,
                annualRate,
                DateTimeOffset.UtcNow);

            stored.Servicing.AccrualEntries.Add(entry);
            stored.Servicing = stored.Servicing with
            {
                Balances = stored.Servicing.Balances with
                {
                    InterestAccruedUnpaid = stored.Servicing.Balances.InterestAccruedUnpaid + interestAmount,
                    CommitmentFeeAccruedUnpaid = stored.Servicing.Balances.CommitmentFeeAccruedUnpaid + commitmentFeeAmount
                },
                LastAccrualDate = request.AccrualDate
            };

            AppendRevision(stored, "InternalEvent", request.AccrualDate, $"Daily accrual posted at annual rate {annualRate:P4}.");
            AppendEvent(stored, "loan.daily-accrual-posted", request.AccrualDate, new
            {
                loanId,
                entry.AccrualEntryId,
                request.AccrualDate,
                entry.InterestAmount,
                entry.CommitmentFeeAmount,
                entry.PenaltyAmount,
                entry.AnnualRateApplied
            }, metadata);
            return Task.FromResult<DailyAccrualEntryDto?>(entry);
        }
    }

    private static void ValidateCreateRequest(CreateLoanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FacilityName))
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Facility name is required."));
        }

        ValidateTerms(request.Terms);
    }

    private static void ValidateTerms(DirectLendingTermsDto terms)
    {
        if (terms.CommitmentAmount <= 0m)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Commitment amount must be positive."));
        }

        if (terms.MaturityDate < terms.OriginationDate)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Maturity date cannot be earlier than origination date."));
        }

        if (terms.RateTypeKind == RateTypeKind.Fixed && terms.FixedAnnualRate is null)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Fixed-rate terms require a fixed annual rate."));
        }

        if (terms.RateTypeKind == RateTypeKind.Floating && string.IsNullOrWhiteSpace(terms.InterestIndexName))
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Floating-rate terms require an interest index name."));
        }
    }

    private static LoanTermsVersionDto CreateTermsVersion(
        int versionNumber,
        DirectLendingTermsDto terms,
        string sourceAction,
        string? amendmentReason,
        DateTimeOffset recordedAt)
    {
        return new LoanTermsVersionDto(
            versionNumber,
            ComputeTermsHash(terms),
            terms,
            sourceAction,
            amendmentReason,
            recordedAt);
    }

    private static string ComputeTermsHash(DirectLendingTermsDto terms)
    {
        var json = JsonSerializer.Serialize(terms, HashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static decimal ResolveAnnualRate(DirectLendingTermsDto terms, RateResetDto? currentRateReset)
    {
        return terms.RateTypeKind switch
        {
            RateTypeKind.Fixed when terms.FixedAnnualRate is { } fixedRate => fixedRate,
            RateTypeKind.Floating when currentRateReset is not null => currentRateReset.AllInRate,
            RateTypeKind.Floating => throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Floating-rate loans require a current rate reset before accrual.")),
            _ => throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Unable to resolve annual rate."))
        };
    }

    private static void EnsureActive(StoredLoan stored)
    {
        if (stored.Status != LoanStatus.Active)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, "Loan must be active before servicing actions can be applied."));
        }
    }

    private static void ApplyPaymentToLots(List<DrawdownLotDto> lots, decimal appliedAmount)
    {
        var remaining = appliedAmount;
        for (var i = 0; i < lots.Count && remaining > 0m; i++)
        {
            var lot = lots[i];
            if (lot.RemainingPrincipal <= 0m)
            {
                continue;
            }

            var appliedToLot = Math.Min(remaining, lot.RemainingPrincipal);
            lots[i] = lot with
            {
                RemainingPrincipal = lot.RemainingPrincipal - appliedToLot
            };
            remaining -= appliedToLot;
        }
    }

    private static void AppendRevision(StoredLoan stored, string sourceType, DateOnly effectiveAsOf, string notes)
    {
        var nextRevision = stored.Servicing.ServicingRevision + 1;
        stored.Servicing.RevisionHistory.Add(new ServicingRevisionDto(
            nextRevision,
            sourceType,
            effectiveAsOf,
            DateTimeOffset.UtcNow,
            notes));

        stored.Servicing = stored.Servicing with
        {
            ServicingRevision = nextRevision
        };
    }

    private static void AppendEvent(StoredLoan stored, string eventType, DateOnly? effectiveDate, object payload, DirectLendingCommandMetadataDto? metadata)
    {
        stored.AggregateVersion++;
        var commandId = metadata?.CommandId ?? Guid.NewGuid();
        stored.History.Add(new LoanEventLineageDto(
            Guid.NewGuid(),
            stored.AggregateVersion,
            eventType,
            EventSchemaVersion: 1,
            effectiveDate,
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(payload, EventJsonOptions),
            CausationId: metadata?.CausationId,
            CorrelationId: metadata?.CorrelationId ?? commandId,
            CommandId: commandId,
            SourceSystem: metadata?.SourceSystem ?? "meridian.direct-lending.in-memory",
            ReplayFlag: metadata?.ReplayFlag ?? false));
    }

    private static LoanContractDetailDto ToContractDetail(StoredLoan stored)
    {
        var currentTerms = stored.TermsVersions[^1].Terms;
        return new LoanContractDetailDto(
            stored.LoanId,
            stored.FacilityName,
            stored.Borrower,
            stored.Status,
            stored.EffectiveDate,
            stored.ActivationDate,
            stored.CloseDate,
            stored.TermsVersions[^1].VersionNumber,
            currentTerms,
            stored.TermsVersions
                .OrderByDescending(static item => item.VersionNumber)
                .ToArray());
    }

    private static LoanServicingStateDto ToServicingState(StoredLoan stored)
    {
        var servicing = stored.Servicing;
        return new LoanServicingStateDto(
            stored.LoanId,
            servicing.Status,
            servicing.CurrentCommitment,
            servicing.TotalDrawn,
            servicing.AvailableToDraw,
            servicing.Balances,
            servicing.DrawdownLots.OrderBy(static lot => lot.DrawdownDate).ToArray(),
            servicing.CurrentRateReset,
            servicing.LastAccrualDate,
            servicing.LastPaymentDate,
            servicing.ServicingRevision,
            servicing.RevisionHistory.OrderByDescending(static item => item.RevisionNumber).ToArray(),
            servicing.AccrualEntries.OrderByDescending(static item => item.AccrualDate).ToArray());
    }

    private static System.Nullable<decimal> ToNullable(decimal? value) =>
        value.HasValue ? new System.Nullable<decimal>(value.Value) : new System.Nullable<decimal>();

    private sealed class StoredLoan
    {
        public StoredLoan(
            Guid loanId,
            string facilityName,
            BorrowerInfoDto borrower,
            DateOnly effectiveDate,
            LoanStatus status,
            DateOnly? activationDate,
            DateOnly? closeDate,
            List<LoanTermsVersionDto> termsVersions,
            ServicingState servicing,
            long aggregateVersion,
            List<LoanEventLineageDto> history)
        {
            LoanId = loanId;
            FacilityName = facilityName;
            Borrower = borrower;
            EffectiveDate = effectiveDate;
            Status = status;
            ActivationDate = activationDate;
            CloseDate = closeDate;
            TermsVersions = termsVersions;
            Servicing = servicing;
            AggregateVersion = aggregateVersion;
            History = history;
        }

        public Guid LoanId { get; }

        public string FacilityName { get; }

        public BorrowerInfoDto Borrower { get; }

        public DateOnly EffectiveDate { get; }

        public LoanStatus Status { get; set; }

        public DateOnly? ActivationDate { get; set; }

        public DateOnly? CloseDate { get; set; }

        public List<LoanTermsVersionDto> TermsVersions { get; }

        public ServicingState Servicing { get; set; }

        public long AggregateVersion { get; set; }

        public List<LoanEventLineageDto> History { get; }
    }

    private sealed record ServicingState(
        LoanStatus Status,
        decimal CurrentCommitment,
        decimal TotalDrawn,
        decimal AvailableToDraw,
        OutstandingBalancesDto Balances,
        List<DrawdownLotDto> DrawdownLots,
        RateResetDto? CurrentRateReset,
        DateOnly? LastAccrualDate,
        DateOnly? LastPaymentDate,
        long ServicingRevision,
        List<ServicingRevisionDto> RevisionHistory,
        List<DailyAccrualEntryDto> AccrualEntries);
}
