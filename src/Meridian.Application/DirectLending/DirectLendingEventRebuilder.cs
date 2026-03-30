using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLendingInterop;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

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

                case "loan.daily-accrual-posted":
                    {
                        EnsureInitialized(contract, servicing, entry.EventType);
                        var accrualEntryId = root.GetProperty("accrualEntryId").GetGuid();
                        var accrualDate = entry.EffectiveDate ?? Deserialize<DateOnly>(root, "accrualDate");
                        var interestAmount = root.GetProperty("interestAmount").GetDecimal();
                        var commitmentFeeAmount = root.GetProperty("commitmentFeeAmount").GetDecimal();
                        var penaltyAmount = root.GetProperty("penaltyAmount").GetDecimal();
                        var annualRateApplied = root.GetProperty("annualRateApplied").GetDecimal();
                        var accrualEntry = new DailyAccrualEntryDto(
                            accrualEntryId,
                            accrualDate,
                            interestAmount,
                            commitmentFeeAmount,
                            penaltyAmount,
                            annualRateApplied,
                            entry.RecordedAt);

                        servicing = servicing! with
                        {
                            Balances = servicing.Balances with
                            {
                                InterestAccruedUnpaid = servicing.Balances.InterestAccruedUnpaid + interestAmount,
                                CommitmentFeeAccruedUnpaid = servicing.Balances.CommitmentFeeAccruedUnpaid + commitmentFeeAmount
                            },
                            LastAccrualDate = accrualDate,
                            ServicingRevision = servicing.ServicingRevision + 1,
                            RevisionHistory = PrependRevision(
                                servicing.RevisionHistory,
                                servicing.ServicingRevision + 1,
                                "InternalEvent",
                                accrualDate,
                                $"Daily accrual posted at annual rate {annualRateApplied:P4}.",
                                entry.RecordedAt),
                            AccrualEntries = servicing.AccrualEntries.Concat([accrualEntry]).OrderByDescending(static item => item.AccrualDate).ToArray()
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
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
