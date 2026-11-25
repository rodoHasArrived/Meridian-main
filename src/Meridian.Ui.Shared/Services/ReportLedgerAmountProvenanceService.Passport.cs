using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using static Meridian.Ui.Shared.Services.AmountPassportFacts;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportLedgerAmountProvenanceService
{
    private static AmountPassportIdentityDto ReportIdentity(string subjectId, LedgerAmountScopeDto scope,
        ReportingAuthoritativeSourceCheckpoint source)
        => new(LedgerKind, subjectId, new(scope.TenantId, scope.CompanyId, source.AccountingBasis,
            scope.FundProfileId, scope.LedgerBookId, scope.PeriodId, OrganizationId: source.OrganizationId),
            Timestamp(source.CutoffUtc), source.CheckpointHash);

    private static AmountPassportReferenceDto ReportReference(string subjectId, ReportLedgerAmountBindingDto amount,
        ReportingAuthoritativeSourceCheckpoint source)
        => new(ReportIdentity(subjectId, amount.Scope, source), amount.Currency, CurrencyUnit, "calculated",
            amount.Amount == 0m, ExactValue: Exact(amount.Amount));

    private static EvidencePacketDto WithPassport(EvidencePacketDto packet, AmountPassportReferenceDto reference,
        ReportingAuthoritativeSourceCheckpoint source, decimal? value, IReadOnlyList<AmountPassportRecordDto> sources,
        IReadOnlyList<AmountPassportRecordDto> history, bool allContributors, bool staleSupport)
    {
        var proof = packet.LedgerAmount!;
        var verified = proof.Status == EvidenceStatusDto.Ready;
        var freshness = new AmountPassportFreshnessDto(staleSupport ? "stale" : verified ? "current" : "blocked",
            packet.GeneratedAt, LedgerEvidencePolicy, staleSupport
                ? "Stale supporting evidence was found on a contributing posting. Report amount proof is blocked."
                : verified
                ? "The certified ledger checkpoint and every contributing source were verified."
                : "The retained population or supporting evidence could not be fully verified.", source.CapturedAtUtc);
        const string calculation = "Ledger trial-balance normal-side balance, rolled up across the account's hierarchy from retained functional postings through the certified cutoff.";
        var passport = Build(reference, packet.Subject.Label, value, Date(source.AsOfDate),
            value is null ? null : calculation, value is null ? null :
                "Functional amounts are read from the certified ledger population. Any retained conversion belongs to its original posting.",
            freshness, new("allowed", "read-only", "The retained report revision is inspectable. Changes require a new governed revision."),
            [
                Component("scope", true, "Exact certified tenant, company, organization, fund, book, basis, period and checkpoint."),
                Component("calculation", value is not null, value is null ? "The retained calculation inputs are missing or invalid." : calculation),
                Component("freshness", verified, freshness.Detail),
                Component("source", allContributors, allContributors ? "All contributing journal identities are retained." : "Contributing journal records are incomplete."),
                Component("evidence", verified, verified ? "Guarded supporting links are available." : "Complete supporting evidence is unavailable."),
                Component("history", history.Count > 0, history.Count > 0 ? "Accessible revisions were checked." : "Report revision history is unavailable."),
                Component("permissions", true, "Inspection is allowed. The retained revision is read-only.")
            ],
            sources, EvidenceLinks(proof), history, proof.Warnings, !verified)
            with
        { AccountingPreparerAttestations = proof.AccountingPreparerAttestations };
        return packet with { LedgerAmount = proof with { AmountPassport = passport } };
    }
}
