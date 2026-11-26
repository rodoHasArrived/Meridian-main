using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class SecurityMasterChangeHistoryRowTests
{
    private static readonly DateTimeOffset ValidFrom = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidTo = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_GoverningEventWithLaterValidFrom_ShowsBoundedPeriod()
    {
        var row = SecurityMasterChangeHistoryRow.From(
            CreateItem() with { ValidFromUtc = ValidFrom, ValidToUtc = ValidTo },
            CultureInfo.InvariantCulture);

        row.ValidFromText.Should().Be(ValidFrom.ToString("g", CultureInfo.InvariantCulture));
        row.ValidToText.Should().Be(ValidTo.ToString("g", CultureInfo.InvariantCulture));
        row.ChangedFieldsSummary.Should().Be("Coupon rate");
        row.StreamVersion.Should().Be(4);
    }

    [Fact]
    public void From_GoverningEventWithoutValidTo_ShowsOpen()
    {
        var row = SecurityMasterChangeHistoryRow.From(CreateItem() with { ValidFromUtc = ValidFrom });

        row.ValidToText.Should().Be(SecurityMasterChangeHistoryRow.OpenText);
    }

    [Fact]
    public void From_CorrectedEvent_ShowsSupersedingVersionInsteadOfPeriodEnd()
    {
        var row = SecurityMasterChangeHistoryRow.From(
            CreateItem() with { ValidFromUtc = ValidFrom, SupersededByStreamVersion = 7 });

        row.ValidToText.Should().Be("superseded by v7");
    }

    [Fact]
    public void From_EntryWithoutValidTime_ShowsNotApplicable()
    {
        var row = SecurityMasterChangeHistoryRow.From(CreateItem());

        row.ValidFromText.Should().Be(SecurityMasterChangeHistoryRow.NotApplicableText);
        row.ValidToText.Should().Be(SecurityMasterChangeHistoryRow.NotApplicableText);
    }

    private static SecurityMasterChangeHistoryItemDto CreateItem() => new(
        ChangeId: "4:TermsAmended",
        StreamVersion: 4,
        EventType: "TermsAmended",
        ChangedAtUtc: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
        EffectiveAtUtc: null,
        Actor: "ops.analyst",
        Origin: "Manual",
        SourceSystem: "workstation",
        SourceRecordId: null,
        Reason: null,
        Summary: "Terms amended.",
        ChangedFields: ["Coupon rate"],
        ChangedFieldsSummary: "Coupon rate");
}
