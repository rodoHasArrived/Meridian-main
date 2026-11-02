using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLendingInterop;
using Meridian.Storage.DirectLending;
using Meridian.Contracts.Integrity;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Reconstructs persisted loan state from retained direct-lending event history.
/// </summary>
public sealed class DirectLendingEventRebuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PersistedDirectLendingState? Rebuild(Guid loanId, IReadOnlyList<LoanEventLineageDto> history)
    {
        if (history.Count == 0)
        {
            return null;
        }

        LoanContractDetailDto? contract = null;
        LoanServicingStateDto? servicing = null;

        foreach (var entry in history.OrderBy(static item => item.AggregateVersion))
        {
            if (entry.EventSchemaVersion != 1)
            {
                throw new InvalidOperationException(
                    $"Unsupported direct-lending event schema version '{entry.EventSchemaVersion}' for event '{entry.EventType}'.");
            }

            using var payload = JsonDocument.Parse(entry.PayloadJson);
            var root = payload.RootElement;

            switch (entry.EventType)
            {
                case "loan.created":
                    {
                        var facilityName = root.GetProperty("facilityName").GetString()
                            ?? throw new InvalidOperationException("loan.created is missing facilityName.");
                        var borrower = Deserialize<BorrowerInfoDto>(root, "borrower");
                        var effectiveDate = Deserialize<DateOnly>(root, "effectiveDate");
                        var terms = Deserialize<DirectLendingTermsDto>(root, "terms");
                        var termsVersion = CreateTermsVersion(1, terms, "LoanCreated", amendmentReason: null, entry.RecordedAt);

                        contract = new LoanContractDetailDto(
                            loanId,
                            facilityName,
                            borrower,
                            LoanStatus.Draft,
                            effectiveDate,
                            ActivationDate: null,
                            CloseDate: null,
                            CurrentTermsVersion: 1,
                            terms,
                            TermsVersions: [termsVersion]);

                        servicing = new LoanServicingStateDto(
                            loanId,
                            LoanStatus.Draft,
                            terms.CommitmentAmount,
                            TotalDrawn: 0m,
                            AvailableToDraw: DirectLendingInterop.CalculateAvailableToDraw(terms.CommitmentAmount, 0m),
                            new OutstandingBalancesDto(0m, 0m, 0m, 0m, 0m),
                            DrawdownLots: [],
                            CurrentRateReset: null,
                            LastAccrualDate: null,
                            LastPaymentDate: null,
                            ServicingRevision: 0,
                            RevisionHistory: [],
                            AccrualEntries: []);
                        break;
                    }

                case "loan.terms-amended":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var terms = Deserialize<DirectLendingTermsDto>(root, "terms");
                        if (DirectLendingPaymentResolver.ValidateTermsAmendment(servicing!, contract!.CurrentTerms, terms) is { } amendmentError)
                            throw new InvalidOperationException(amendmentError);
                        var versionNumber = root.GetProperty("versionNumber").GetInt32();
                        var amendmentReason = GetOptionalString(root, "amendmentReason");
                        var termsVersion = CreateTermsVersion(versionNumber, terms, "TermsAmended", amendmentReason, entry.RecordedAt);

                        contract = contract! with
                        {
                            CurrentTermsVersion = versionNumber,
                            CurrentTerms = terms,
                            TermsVersions = contract.TermsVersions
                                .Concat([termsVersion])
                                .OrderByDescending(static item => item.VersionNumber)
                                .ToArray()
                        };

                        servicing = servicing! with
                        {
                            CurrentCommitment = terms.CommitmentAmount,
                            AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(terms.CommitmentAmount, servicing.TotalDrawn)
                        };
                        break;
                    }

                case "loan.activated":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var activationDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "activationDate");

                        contract = contract! with
                        {
                            Status = LoanStatus.Active,
                            ActivationDate = activationDate
                        };

                        servicing = servicing! with
                        {
                            Status = LoanStatus.Active
                        };
                        break;
                    }

                case "loan.drawdown-booked":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var lotId = root.GetProperty("lotId").GetGuid();
                        var amount = root.GetProperty("amount").GetDecimal();
                        var tradeDate = Deserialize<DateOnly>(root, "tradeDate");
                        var settleDate = Deserialize<DateOnly>(root, "settleDate");
                        var externalRef = GetOptionalString(root, "externalRef");

                        var lot = new DrawdownLotDto(lotId, tradeDate, settleDate, amount, amount, externalRef);
                        var updatedTotalDrawn = servicing!.TotalDrawn + amount;
                        servicing = servicing with
                        {
                            TotalDrawn = updatedTotalDrawn,
                            AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, updatedTotalDrawn),
                            Balances = servicing.Balances with
                            {
                                PrincipalOutstanding = servicing.Balances.PrincipalOutstanding + amount
                            },
                            DrawdownLots = servicing.DrawdownLots.Concat([lot]).OrderBy(static item => item.DrawdownDate).ToArray(),
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                settleDate,
                                $"Drawdown booked for {amount:0.00}.",
                                entry.RecordedAt)
                        };
                        break;
                    }

                case "loan.rate-reset-applied":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        var observedRate = root.GetProperty("observedRate").GetDecimal();
                        var spreadBps = root.GetProperty("spreadBps").GetDecimal();
                        var allInRate = root.GetProperty("allInRate").GetDecimal();
                        var sourceRef = GetOptionalString(root, "sourceRef");

                        servicing = servicing! with
                        {
                            CurrentRateReset = new RateResetDto(
                                effectiveDate,
                                contract!.CurrentTerms.InterestIndexName ?? "FloatingIndex",
                                observedRate,
                                spreadBps,
                                allInRate,
                                sourceRef),
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                effectiveDate,
                                $"Rate reset applied at {allInRate:P4}.",
                                entry.RecordedAt)
                        };
                        break;
                    }

                case "loan.principal-payment-applied":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var appliedAmount = root.GetProperty("appliedAmount").GetDecimal();
                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        var principalAfter = DirectLendingInterop.ApplyPrincipalPayment(servicing!.Balances.PrincipalOutstanding, appliedAmount);
                        var totalDrawnAfter = Math.Max(0m, servicing.TotalDrawn - appliedAmount);

                        servicing = servicing with
                        {
                            TotalDrawn = totalDrawnAfter,
                            AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                            Balances = servicing.Balances with
                            {
                                PrincipalOutstanding = principalAfter
                            },
                            DrawdownLots = ApplyPaymentToLots(servicing.DrawdownLots, appliedAmount),
                            LastPaymentDate = effectiveDate,
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                effectiveDate,
                                $"Principal payment applied for {appliedAmount:0.00}.",
                                entry.RecordedAt)
                        };
                        break;
                    }


                case "loan.mixed-payment-applied":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var receipt = DirectLendingCashJournal.Read(root);
                        DirectLendingCashJournal.ValidateTerms(receipt, contract!);
                        var resolution = receipt.Resolution
                            ?? throw new InvalidOperationException("Mixed-payment replay requires its retained allocation resolution.");
                        var breakdown = resolution.Breakdown;
                        var before = servicing!.Balances;
                        if (receipt.IsVersioned &&
                            (receipt.TermsVersion != contract!.CurrentTermsVersion ||
                             breakdown.ToPrincipal > before.PrincipalOutstanding ||
                             breakdown.ToInterest > before.InterestAccruedUnpaid ||
                             breakdown.ToCommitmentFee > before.CommitmentFeeAccruedUnpaid ||
                             breakdown.ToFees > before.FeesAccruedUnpaid ||
                             breakdown.ToPenalty > before.PenaltyAccruedUnpaid))
                            throw new InvalidOperationException("Retained mixed-payment allocations exceed the balances or terms version at the event.");

                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        var totalDrawnAfter = Math.Max(0m, servicing.TotalDrawn - breakdown.ToPrincipal);
                        DirectLendingCashJournal.ValidateEffectiveDate(receipt, effectiveDate);
                        var prepaidLots = receipt.IsVersioned
                            ? DirectLendingPrepaidInterest.CreateReceiptLots(servicing,
                                contract!.TermsVersions.Single(item => item.VersionNumber == receipt.TermsVersion).Terms,
                                receipt.CashTransactionId!.Value, effectiveDate, resolution.PrepaidInterestAmount)
                            : before.PrepaidInterestLots;
                        servicing = servicing with
                        {
                            TotalDrawn = totalDrawnAfter,
                            AvailableToDraw = DirectLendingInterop.CalculateAvailableToDraw(servicing.CurrentCommitment, totalDrawnAfter),
                            Balances = before with
                            {
                                // Legacy events retain the original clamp semantics; replay is not a correction journal.
                                PrincipalOutstanding = Math.Max(0m, before.PrincipalOutstanding - breakdown.ToPrincipal),
                                InterestAccruedUnpaid = Math.Max(0m, before.InterestAccruedUnpaid - breakdown.ToInterest),
                                CommitmentFeeAccruedUnpaid = Math.Max(0m, before.CommitmentFeeAccruedUnpaid - breakdown.ToCommitmentFee),
                                FeesAccruedUnpaid = Math.Max(0m, before.FeesAccruedUnpaid - breakdown.ToFees),
                                PenaltyAccruedUnpaid = Math.Max(0m, before.PenaltyAccruedUnpaid - breakdown.ToPenalty),
                                UnappliedCash = before.UnappliedCash + (receipt.IsVersioned ? resolution.UnappliedAmount : 0m),
                                PrepaidInterest = before.PrepaidInterest + (receipt.IsVersioned ? resolution.PrepaidInterestAmount : 0m),
                                PrepaidInterestLots = prepaidLots
                            },
                            DrawdownLots = ApplyPaymentToLots(servicing.DrawdownLots, breakdown.ToPrincipal),
                            LastPaymentDate = effectiveDate,
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                effectiveDate,
                                $"Mixed payment applied for {receipt.Amount:0.00}.",
                                entry.RecordedAt)
                        };
                        break;
                    }

                case "loan.fee-assessed":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var amount = root.GetProperty("amount").GetDecimal();
                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        servicing = servicing! with
                        {
                            Balances = servicing.Balances with { FeesAccruedUnpaid = servicing.Balances.FeesAccruedUnpaid + amount },
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                effectiveDate,
                                $"Fee assessed for {amount:0.00}.",
                                entry.RecordedAt)
                        };
                        break;
                    }

                case "loan.prepayment-penalty-charged":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        var penaltyAmount = root.GetProperty("penaltyAmount").GetDecimal();

                        servicing = servicing! with
                        {
                            Balances = servicing.Balances with
                            {
                                PenaltyAccruedUnpaid = servicing.Balances.PenaltyAccruedUnpaid + penaltyAmount
                            },
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                effectiveDate,
                                $"Prepayment penalty charged for {penaltyAmount:0.00}.",
                                entry.RecordedAt)
                        };
                        break;
                    }
                case "loan.daily-accrual-posted":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var accrualEntryId = root.GetProperty("accrualEntryId").GetGuid();
                        var accrualDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "accrualDate");
                        var interestAmount = root.GetProperty("interestAmount").GetDecimal();
                        var commitmentFeeAmount = root.GetProperty("commitmentFeeAmount").GetDecimal();
                        var penaltyAmount = root.GetProperty("penaltyAmount").GetDecimal();
                        var annualRateApplied = root.GetProperty("annualRateApplied").GetDecimal();
                        // Events recorded before PIK accrual wiring carry no pikInterestAmount.
                        var pikInterestAmount =
                            root.TryGetProperty("pikInterestAmount", out var pikElement) &&
                            pikElement.ValueKind == JsonValueKind.Number
                                ? pikElement.GetDecimal()
                                : 0m;
                        var prepaidRelease = DirectLendingCashJournal.ReadAccrualRelease(root, interestAmount);
                        DirectLendingCashJournal.ValidateAccrualDate(prepaidRelease, accrualDate);
                        if (prepaidRelease.AppliedAmount > 0m && prepaidRelease.Currency != contract!.CurrentTerms.BaseCurrency)
                            throw new InvalidOperationException("Retained prepaid-interest release currency differs from the contract at accrual.");
                        var prepaidApplication = DirectLendingPrepaidInterest.ApplyRecordedRelease(servicing!, interestAmount,
                            accrualDate, prepaidRelease.AppliedAmount, prepaidRelease.Releases);
                        var accrualEntry = new DailyAccrualEntryDto(
                            accrualEntryId,
                            accrualDate,
                            interestAmount,
                            commitmentFeeAmount,
                            penaltyAmount,
                            annualRateApplied,
                            root.TryGetProperty("recordedAt", out var retainedAccrualTime)
                                ? retainedAccrualTime.GetDateTimeOffset() : entry.RecordedAt,
                            pikInterestAmount,
                            prepaidRelease.AppliedAmount,
                            prepaidRelease.Releases);

                        servicing = servicing! with
                        {
                            Balances = servicing.Balances with
                            {
                                PrincipalOutstanding = servicing.Balances.PrincipalOutstanding + pikInterestAmount,
                                InterestAccruedUnpaid = servicing.Balances.InterestAccruedUnpaid + interestAmount - prepaidRelease.AppliedAmount,
                                CommitmentFeeAccruedUnpaid = servicing.Balances.CommitmentFeeAccruedUnpaid + commitmentFeeAmount,
                                PenaltyAccruedUnpaid = servicing.Balances.PenaltyAccruedUnpaid + penaltyAmount,
                                PrepaidInterest = servicing.Balances.PrepaidInterest - prepaidRelease.AppliedAmount,
                                PrepaidInterestLots = prepaidApplication.Lots
                            },
                            LastAccrualDate = accrualDate,
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                accrualDate,
                                pikInterestAmount > 0m
                                    ? $"Daily PIK accrual capitalized at annual rate {annualRateApplied:P4}."
                                    : $"Daily accrual posted at annual rate {annualRateApplied:P4}.",
                                entry.RecordedAt),
                            AccrualEntries = servicing.AccrualEntries.Concat([accrualEntry]).OrderByDescending(static item => item.AccrualDate).ToArray()
                        };
                        break;
                    }

                case "loan.pik-toggled":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var enablePik = root.GetProperty("enablePik").GetBoolean();
                        var effectiveDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "effectiveDate");
                        var reason =
                            root.TryGetProperty("reason", out var reasonElement) &&
                            reasonElement.ValueKind == JsonValueKind.String &&
                            reasonElement.GetString() is { Length: > 0 } reasonText
                                ? reasonText
                                : enablePik ? "PIK enabled" : "PIK disabled";

                        servicing = servicing! with
                        {
                            IsPikToggled = enablePik,
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "PikToggle",
                                effectiveDate,
                                reason,
                                entry.RecordedAt)
                        };
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unsupported direct-lending event type '{entry.EventType}' during rebuild.");
            }
        }

        EnsureInitialized(contract, servicing, "rebuild");
        var latestVersion = history.Max(static item => item.AggregateVersion);
        return new PersistedDirectLendingState(loanId, latestVersion, contract!, servicing!);
    }

    private static void EnsureInitialized(LoanContractDetailDto? contract, LoanServicingStateDto? servicing, string eventType)
    {
        if (contract is null || servicing is null)
        {
            throw new InvalidOperationException($"Direct lending rebuild encountered '{eventType}' before 'loan.created'.");
        }
    }

    private static T Deserialize<T>(JsonElement root, string propertyName)
    {
        var element = root.GetProperty(propertyName);
        return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize '{propertyName}' as {typeof(T).Name}.");
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
        var bytes = Sha256Digest.ComputeBytesUtf8(json);
        return Convert.ToHexString(bytes);
    }

    private static IReadOnlyList<DrawdownLotDto> ApplyPaymentToLots(IReadOnlyList<DrawdownLotDto> lots, decimal appliedAmount)
    {
        var updated = lots.ToArray();
        var remaining = appliedAmount;
        for (var i = 0; i < updated.Length && remaining > 0m; i++)
        {
            var lot = updated[i];
            if (lot.RemainingPrincipal <= 0m)
            {
                continue;
            }

            var appliedToLot = Math.Min(remaining, lot.RemainingPrincipal);
            updated[i] = lot with
            {
                RemainingPrincipal = lot.RemainingPrincipal - appliedToLot
            };
            remaining -= appliedToLot;
        }

        return updated;
    }

    private static IReadOnlyList<ServicingRevisionDto> PrependRevision(
        IReadOnlyList<ServicingRevisionDto> history,
        long revisionNumber,
        string sourceType,
        DateOnly effectiveAsOf,
        string notes,
        DateTimeOffset createdAt)
    {
        return new[]
        {
            new ServicingRevisionDto(
                revisionNumber,
                sourceType,
                effectiveAsOf,
                createdAt,
                notes)
        }.Concat(history).ToArray();
    }
}
