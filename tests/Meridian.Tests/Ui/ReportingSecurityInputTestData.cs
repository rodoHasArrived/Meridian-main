using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Reporting;

namespace Meridian.Tests.Ui;

internal static class ReportingSecurityInputTestData
{
    // Deliberately frozen pre-format receipt bytes: live fixtures must declare a family, while
    // these inputs prove historical hashes and report reads survive the stricter live contract.
    internal static SecurityCashFlowCalculationReceipt Receipt(Guid? securityId = null, decimal coupon = 5.25m)
    {
        var id = securityId ?? Guid.NewGuid();
        var cutoff = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var security = new SecurityDetailDto(id, "Bond", SecurityStatusDto.Active, "Synthetic retained bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                maturity = "2030-07-01",
                couponRate = coupon,
                scheduleConventions = new { couponRateUnit = "Percent" },
                isCallable = false,
                subclass = "Corporate"
            }), [], [], 2, cutoff.AddDays(-1), null);
        var source = new SecurityCashFlowSourceDto(id, StructuredCashFlowSourceKind.CalculatedBullet, cutoff, false, null, null);
        var projection = new StructuredCashFlowProjectionDto(id, source.SourceKind, StructuredCashFlowScenario.Base,
            cutoff, [], StructuredCashFlowStaleness.Fresh, cutoff);
        var receipt = new SecurityCashFlowCalculationReceipt("bond-cash-flow-context-v5", security, source,
            StructuredCashFlowScenario.Base, new BondCalculationContext(cutoff, KnownAt: cutoff, SecurityVersion: security.Version), [], projection, "");
        return receipt with { ContentSha256 = SecurityMasterCashFlowService.ReceiptHash(receipt) };
    }

    internal static JournalEntry WithReceipt(JournalEntry entry, SecurityCashFlowCalculationReceipt receipt)
        => new(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines, entry.Metadata with
        {
            SecurityId = receipt.Security.SecurityId,
            Tags = new Dictionary<string, string>
            {
                [ReportingSecurityInputEvidence.ReceiptTag] = receipt.ContentSha256,
                ["cash-flow.calculationModel"] = receipt.ModelVersion
            },
            EvidenceReferences = [new($"cash-flow-receipt:{receipt.ContentSha256}",
                $"evidence://security-master/cash-flow-receipts/{receipt.ContentSha256}", "cash-flow-calculation-receipt",
                "SecurityMaster", receipt.Context.AsOf, "synthetic-calculator", SubjectId: receipt.Security.SecurityId.ToString("D"), ContentHash: receipt.ContentSha256)]
        });

    internal static ReportingRetainedSecurityInputs Inputs(params (JournalEntry Entry, SecurityCashFlowCalculationReceipt Receipt)[] values)
    {
        var bindings = values.Select(value =>
        {
            var payload = SecurityMasterCashFlowService.ExportReceiptEvidence(value.Receipt);
            return new ReportingSecurityInputBinding(value.Entry.JournalEntryId, value.Receipt.Security.SecurityId,
                value.Receipt.ContentSha256, payload, Sha256Digest.ComputeUtf8(payload));
        }).OrderBy(static value => value.JournalEntryId).ToImmutableArray();
        var inputs = new ReportingRetainedSecurityInputs(bindings, [], "");
        return inputs with { ContentHashSha256 = ReportingSecurityInputEvidence.ComputeHash(inputs) };
    }
}
