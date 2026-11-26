using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Security Master review F2: a manual journal line's ledger account key round-trips through
/// reversal, rebook and correction drafts, and must agree with the instrument the line names.
/// </summary>
public sealed class ManualJournalLedgerAccountSecurityIdTests
{
    private static readonly Guid MetaSecurityId = Guid.Parse("f2f2f2f2-0000-4000-8000-0000000000b1");

    [Fact]
    public void Line_LedgerAccountSecurityId_RoundTripsThroughJsonAndIsOmittedWhenNull()
    {
        var keyed = Line() with { LedgerAccountSecurityId = MetaSecurityId };
        var json = System.Text.Json.JsonSerializer.Serialize(keyed, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        System.Text.Json.JsonSerializer.Deserialize<ManualJournalEntryLineDto>(json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!
            .LedgerAccountSecurityId.Should().Be(MetaSecurityId);
        System.Text.Json.JsonSerializer.Serialize(Line(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            .Should().NotContain("ledgerAccountSecurityId", "drafts saved before F2 keep their digests");
    }

    [Fact]
    public void CorrectionLine_KeepsItsAccountKey_WhenCopiedWithRecordWith()
    {
        // Server-built reversal/rebook/inverse lines are produced with `line with { ... }`.
        var posted = Line() with { LedgerAccountSecurityId = MetaSecurityId };

        var inverse = posted with { LineId = "rebook-inverse-1", Side = AccountingTemplateLineSideDto.Credit };

        inverse.LedgerAccountSecurityId.Should().Be(MetaSecurityId);
        ManualJournalEntryWorkbenchService.FindLedgerAccountSecurityMismatch(Draft(inverse)).Should().BeNull();
    }

    [Fact]
    public void EditedCorrectionLine_NamingAnotherInstrument_IsRefused()
    {
        var edited = Line() with { SecurityId = Guid.NewGuid(), LedgerAccountSecurityId = MetaSecurityId };

        ManualJournalEntryWorkbenchService.FindLedgerAccountSecurityMismatch(Draft(edited))
            .Should().Contain("keep the posted line's account identity");
    }

    [Fact]
    public void KeyedLine_WithoutAnInstrument_OrWithAnEmptyKey_IsRefused()
    {
        ManualJournalEntryWorkbenchService.FindLedgerAccountSecurityMismatch(
                Draft(Line() with { SecurityId = null, LedgerAccountSecurityId = MetaSecurityId }))
            .Should().Contain("names no instrument");
        ManualJournalEntryWorkbenchService.FindLedgerAccountSecurityMismatch(
                Draft(Line() with { LedgerAccountSecurityId = Guid.Empty }))
            .Should().Contain("empty ledger account Security Master identity");
    }

    [Fact]
    public void InstrumentFromDimensions_SatisfiesTheKey_AndUnkeyedLinesAreUnaffected()
    {
        var viaDimensions = Line() with
        {
            SecurityId = null,
            Dimensions = new LedgerDimensionSetDto(InstrumentId: MetaSecurityId),
            LedgerAccountSecurityId = MetaSecurityId
        };

        ManualJournalEntryWorkbenchService.FindLedgerAccountSecurityMismatch(Draft(viaDimensions, Line())).Should().BeNull();
    }

    private static ManualJournalEntryLineDto Line() => new(
        "line-1",
        AccountingTemplateLineSideDto.Debit,
        100m,
        "USD",
        "Assets/Securities",
        SecurityId: MetaSecurityId,
        LedgerAccountSymbol: "META");

    private static ManualJournalEntryDraftDto Draft(params ManualJournalEntryLineDto[] lines)
        => ManualJournalDraftFixture with { Lines = lines };

    private static readonly ManualJournalEntryDraftDto ManualJournalDraftFixture =
        System.Text.Json.JsonSerializer.Deserialize<ManualJournalEntryDraftDto>("{}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
}
