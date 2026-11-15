using System.Collections.Immutable;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class LedgerReportingAuthoritativeSource
{
    private readonly ISecurityCashFlowReceiptStore? _securityReceiptStore;

    private async Task<ReportingRetainedSecurityInputs> CaptureSecurityInputsAsync(
        IReadOnlyList<LedgerJournalEntryRecord> records, CancellationToken ct)
    {
        var bindings = ImmutableArray.CreateBuilder<ReportingSecurityInputBinding>();
        var gaps = ImmutableArray.CreateBuilder<ReportingSecurityInputGap>();
        foreach (var record in records.OrderBy(static record => record.Entry.JournalEntryId))
        {
            var journal = record.Entry;
            if (!ReportingSecurityInputEvidence.HasInputIdentity(journal))
                continue;
            void Gap(string reason) => gaps.Add(new(journal.JournalEntryId, journal.Metadata.SecurityId, reason));
            var hash = ReportingSecurityInputEvidence.ResolveReceiptIdentity(journal);
            if (journal.Metadata.SecurityId is not { } securityId || securityId == Guid.Empty || hash is null)
            {
                Gap("The journal has no unambiguous security and calculation-receipt binding.");
                continue;
            }
            if (_securityReceiptStore is null)
            {
                Gap("The calculation receipt store was unavailable at report capture.");
                continue;
            }
            try
            {
                var receipt = await _securityReceiptStore.GetAsync(hash, ct).ConfigureAwait(false);
                if (receipt is null || receipt.ContentSha256 != hash || receipt.Security.SecurityId != securityId
                    || receipt.Source.SecurityId != securityId || receipt.Projection.SecurityId != securityId
                    || receipt.Context.SecurityVersion is { } version && version != receipt.Security.Version
                    || receipt.Context.AsOf != receipt.Projection.AsOf || receipt.Scenario != receipt.Projection.Scenario
                    || receipt.Source.SourceKind != receipt.Projection.SourceKind
                    || journal.Metadata.Tags?.TryGetValue("cash-flow.calculationModel", out var model) == true && receipt.ModelVersion != model)
                {
                    Gap("The exact calculation receipt is missing or contradicts its journal.");
                    continue;
                }
                var payload = SecurityMasterCashFlowService.ExportReceiptEvidence(receipt);
                bindings.Add(new(journal.JournalEntryId, securityId, hash, payload, Sha256Digest.ComputeUtf8(payload)));
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
            {
                Gap("The retained calculation receipt could not be verified at report capture.");
            }
        }
        var inputs = new ReportingRetainedSecurityInputs(bindings.ToImmutable(), gaps.ToImmutable(), "");
        return inputs with { ContentHashSha256 = ReportingSecurityInputEvidence.ComputeHash(inputs) };
    }
}
