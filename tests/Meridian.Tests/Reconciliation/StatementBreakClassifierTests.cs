using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementBreakClassifierTests
{
    [Theory]
    [InlineData(StatementBreakClassificationType.MissingInternalPosition)]
    [InlineData(StatementBreakClassificationType.MissingBrokerPosition)]
    [InlineData(StatementBreakClassificationType.PositionQuantityVariance)]
    [InlineData(StatementBreakClassificationType.CashBalanceVariance)]
    [InlineData(StatementBreakClassificationType.MissingInternalTransaction)]
    [InlineData(StatementBreakClassificationType.MissingBrokerTransaction)]
    [InlineData(StatementBreakClassificationType.TransactionTimingVariance)]
    [InlineData(StatementBreakClassificationType.SecurityMappingBreak)]
    [InlineData(StatementBreakClassificationType.AccountMappingBreak)]
    [InlineData(StatementBreakClassificationType.CurrencyMismatch)]
    [InlineData(StatementBreakClassificationType.FeeOrTaxVariance)]
    public void Classify_SupportsCanonicalStatementBreakTypes(StatementBreakClassificationType breakType)
    {
        var classifier = new StatementBreakClassifier();

        var result = classifier.Classify(new StatementBreakClassificationRequest(
            breakType,
            StatementAmount: 100m,
            InternalAmount: 0m,
            QuantityDelta: 1m,
            StatementCurrency: "USD",
            InternalCurrency: breakType == StatementBreakClassificationType.CurrencyMismatch ? "EUR" : "USD"));

        Assert.Equal(breakType, result.BreakType);
        Assert.NotEqual(default, result.WorkflowRationale);
    }

    [Theory]
    [InlineData(StatementBreakClassificationType.MissingInternalPosition)]
    [InlineData(StatementBreakClassificationType.MissingBrokerPosition)]
    [InlineData(StatementBreakClassificationType.PositionQuantityVariance)]
    [InlineData(StatementBreakClassificationType.CashBalanceVariance)]
    public void Classify_AssignsHighSeverityToMaterialCashAndPositionBreaks(StatementBreakClassificationType breakType)
    {
        var classifier = new StatementBreakClassifier();

        var result = classifier.Classify(new StatementBreakClassificationRequest(
            breakType,
            StatementAmount: 125m,
            InternalAmount: 0m,
            QuantityDelta: 5m));

        Assert.Equal(ReconciliationBreakSeverity.High, result.Severity);
        Assert.True(result.IsMaterial);
        Assert.True(result.ShouldCreateCaseQueueItem);
    }

    [Theory]
    [InlineData(StatementBreakClassificationType.TransactionTimingVariance, StatementBreakRecommendedAction.VerifySettlementDate)]
    [InlineData(StatementBreakClassificationType.FeeOrTaxVariance, StatementBreakRecommendedAction.InspectLedgerTransaction)]
    public void Classify_AssignsMediumSeverityToTransactionTimingAndFeeDifferences(
        StatementBreakClassificationType breakType,
        StatementBreakRecommendedAction expectedAction)
    {
        var classifier = new StatementBreakClassifier();

        var result = classifier.Classify(new StatementBreakClassificationRequest(
            breakType,
            StatementAmount: 25m,
            InternalAmount: 0m));

        Assert.Equal(ReconciliationBreakSeverity.Medium, result.Severity);
        Assert.Equal(expectedAction, result.RecommendedAction);
        Assert.True(result.ShouldCreateCaseQueueItem);
    }

    [Theory]
    [InlineData(StatementBreakClassificationType.SecurityMappingBreak)]
    [InlineData(StatementBreakClassificationType.AccountMappingBreak)]
    public void Classify_AssignsLowSeverityToOptionalUnmappedMetadataAndSuppressesCaseQueue(
        StatementBreakClassificationType breakType)
    {
        var classifier = new StatementBreakClassifier();

        var result = classifier.Classify(new StatementBreakClassificationRequest(
            breakType,
            IsOptionalMetadata: true));

        Assert.Equal(ReconciliationBreakSeverity.Low, result.Severity);
        Assert.False(result.IsMaterial);
        Assert.False(result.ShouldCreateCaseQueueItem);
    }

    [Fact]
    public void Classify_SuppressesCaseQueueForResolvedMaterialBreaks()
    {
        var classifier = new StatementBreakClassifier();

        var result = classifier.Classify(new StatementBreakClassificationRequest(
            StatementBreakClassificationType.CashBalanceVariance,
            StatementAmount: 100m,
            InternalAmount: 0m,
            Status: "Resolved"));

        Assert.True(result.IsMaterial);
        Assert.False(result.IsUnresolved);
        Assert.False(result.ShouldCreateCaseQueueItem);
    }

    [Fact]
    public void FromStatementRow_ClassifiesCashRowsForCashPostingReview()
    {
        var row = new NormalizedStatementRow(
            "row-1",
            StatementRowKind.CashBalance,
            string.Empty,
            0m,
            250m,
            DateTimeOffset.UtcNow,
            "USD",
            "fingerprint",
            new Dictionary<string, string>());

        var result = new StatementBreakClassifier().Classify(StatementBreakClassificationRequest.FromStatementRow(row));

        Assert.Equal(StatementBreakClassificationType.CashBalanceVariance, result.BreakType);
        Assert.Equal(StatementBreakRecommendedAction.ReviewCashPosting, result.RecommendedAction);
        Assert.Equal("review cash posting", result.RecommendedActionText);
        Assert.True(result.ShouldCreateCaseQueueItem);
    }
}
