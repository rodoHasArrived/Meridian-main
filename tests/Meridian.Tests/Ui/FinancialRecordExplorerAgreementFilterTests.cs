using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

[Trait("Category", "Unit")]
public sealed class FinancialRecordExplorerAgreementFilterTests
{
    private static readonly Guid AgreementId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OtherAgreementId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AgreementFilters_KeepOnlyTheExactAgreementAndVersion(bool savedView)
    {
        var filters = new List<FinancialRecordExplorerFilterDto>();
        FinancialRecordExplorerReadService.AddDimensionFilters(filters,
        [
            new LedgerDimensionSetDto
            {
                IntercompanyAgreementId = AgreementId,
                IntercompanyAgreementVersion = 1
            }
        ]);
        filters.Should().HaveCount(2);
        var rows = new[]
        {
            Row("version-ten", AgreementId, "10"),
            Row("other-agreement", OtherAgreementId, "1"),
            Row("missing-dimensions", null, null),
            Row("exact-match", AgreementId, "1")
        };
        var explorer = Explorer(rows);
        if (savedView)
        {
            explorer = explorer with
            {
                SavedViews = [new("agreement-view", "Agreement", "", false, false, filters)]
            };
        }

        var result = FinancialRecordExplorerReadService.ApplyExplorerQuery(explorer,
            savedView
                ? new FinancialRecordExplorerQueryDto(ViewId: "agreement-view")
                : new FinancialRecordExplorerQueryDto(Filters: filters));

        result.Rows.Select(row => row.RecordId).Should().Equal("exact-match");
        result.SelectedRecord!.RecordId.Should().Be("exact-match");
        result.SummaryItems.Should().Contain(item => item.Label == "Visible records" && item.Value == "1");
        result.RecordGraph.Nodes.Should().Contain(node => node.Label == "exact-match");
        result.RecordGraph.Nodes.Should().NotContain(node => node.Label == "version-ten" || node.Label == "other-agreement");
    }

    [Theory]
    [InlineData("intercompanyAgreementId", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "exact-match")]
    [InlineData("IntercompanyAgreementId", "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA", "exact-match")]
    [InlineData("intercompanyAgreementId", "aaaaaaaa", null)]
    [InlineData("intercompanyAgreementVersion", "1", "exact-match")]
    [InlineData("intercompanyAgreementVersion", "11", null)]
    public void CanonicalAgreementFilterIds_DoNotSearchOtherFields(string filterId, string value, string? expectedRow)
    {
        var explorer = Explorer(
        [
            Row("version-ten", OtherAgreementId, "10"),
            Row("missing-dimensions", null, null),
            Row("exact-match", AgreementId, "1")
        ]);

        var result = FinancialRecordExplorerReadService.ApplyExplorerQuery(explorer,
            new FinancialRecordExplorerQueryDto(Filters: [new(filterId, filterId, value)]));

        var expectedRows = expectedRow is null ? Array.Empty<string>() : [expectedRow];
        result.Rows.Select(row => row.RecordId).Should().Equal(expectedRows);
        result.SelectedRecord?.RecordId.Should().Be(expectedRow);
    }

    private static FinancialRecordExplorerDto Explorer(IReadOnlyList<FinancialRecordExplorerRowDto> rows)
        => new("ledger", "Ledger", "", "Synthetic", false, "", [], [], [], [], [], rows,
            rows.FirstOrDefault()?.Detail, [], new([], []));

    private static FinancialRecordExplorerRowDto Row(string recordId, Guid? agreementId, string? version)
    {
        var fields = new List<FinancialRecordExplorerSummaryItemDto>
        {
            new("Unrelated evidence", $"{AgreementId:D} 1 11")
        };
        if (agreementId.HasValue)
            fields.Add(new("Agreement", agreementId.Value.ToString("D")));
        if (version is not null)
            fields.Add(new("Agreement version", version));
        var detail = new FinancialRecordExplorerSelectedRecordDto(recordId, "ledger", recordId, "", "",
            FinancialRecordExplorerTone.Default, fields, [], [], []);
        return new(recordId, "ledger", recordId, "Synthetic", "Available", FinancialRecordExplorerTone.Default,
            [new("balance", "11")], detail);
    }
}
