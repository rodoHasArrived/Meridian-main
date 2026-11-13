using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts.Ledger;

public sealed class LedgerDimensionReferenceUnionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegalAgreementAndAccountingArrangement_RoundTripAsDistinctTypedReferences()
    {
        var dimensions = new LedgerDimensionSetDto(EntityId: "entity-a")
        {
            IntercompanyAgreementId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            IntercompanyAgreementVersion = 3,
            AccountingArrangementId = "receivable-settlement-profile",
            AccountingArrangementRevisionId = "approved-profile-revision",
            ChartAccountNodeId = "account-receivable",
            AccountBindingRevisionId = "binding-revision",
            IntercompanyTransactionId = "invoice-100"
        };

        var serialized = JsonSerializer.Serialize(dimensions, Json);
        using var document = JsonDocument.Parse(serialized);
        document.RootElement.GetProperty("intercompanyAgreementId").GetGuid().Should().Be(dimensions.IntercompanyAgreementId!.Value);
        document.RootElement.GetProperty("intercompanyAgreementVersion").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("accountingArrangementId").GetString().Should().Be(dimensions.AccountingArrangementId);
        document.RootElement.GetProperty("accountingArrangementRevisionId").GetString().Should().Be(dimensions.AccountingArrangementRevisionId);
        document.RootElement.TryGetProperty("agreementId", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("agreementRevisionId", out _).Should().BeFalse();
        var restored = JsonSerializer.Deserialize<LedgerDimensionSetDto>(serialized, Json)!;
        restored.Should().BeEquivalentTo(dimensions);
        LedgerDimensionTags.HasAnyDimension(restored).Should().BeTrue();
    }

    [Fact]
    public void AbsentOptionalReferences_IntroduceNoLegacyJsonFields()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new LedgerDimensionSetDto(EntityId: "entity-a"), Json));
        foreach (var field in new[] { "intercompanyAgreementId", "intercompanyAgreementVersion", "accountingArrangementId",
                     "accountingArrangementRevisionId", "chartAccountNodeId", "accountBindingRevisionId", "intercompanyTransactionId" })
            document.RootElement.TryGetProperty(field, out _).Should().BeFalse("absent optional references must not change retained legacy JSON");
        typeof(LedgerDimensionSetDto).GetProperty("AgreementId").Should().BeNull("legal agreements and operational profiles must not share an ambiguous alias");
        typeof(LedgerDimensionSetDto).GetProperty("AgreementRevisionId").Should().BeNull();
    }
}
