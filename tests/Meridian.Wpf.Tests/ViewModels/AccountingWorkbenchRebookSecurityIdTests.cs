using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.ViewModels.Accounting;
using Xunit;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Security Master review F2: the WPF rebook builder must carry a posted line's SecurityId-keyed
/// ledger account identity back to the server unchanged.
/// </summary>
public sealed class AccountingWorkbenchRebookSecurityIdTests
{
    [Fact]
    public void BuildManualJournalRebookLines_KeepsTheLedgerAccountSecurityId()
    {
        var securityId = Guid.Parse("f2f2f2f2-0000-4000-8000-0000000000b2");
        var posted = new ManualJournalEntryLineDto("line-1", AccountingTemplateLineSideDto.Debit, 100m, "USD",
            "Assets/Securities", SecurityId: securityId, LedgerAccountSymbol: "META")
        {
            LedgerAccountSecurityId = securityId
        };
        var draft = System.Text.Json.JsonSerializer.Deserialize<ManualJournalEntryDraftDto>("{}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))! with
        {
            Lines = [posted],
            EvidenceLinks = ["evidence://rebook"]
        };

        var rebook = AccountingWorkbenchPresentation.BuildManualJournalRebookLines(draft);

        rebook.Should().ContainSingle().Which.LedgerAccountSecurityId.Should().Be(securityId);
        rebook[0].LedgerAccountSymbol.Should().Be("META");
    }
}
