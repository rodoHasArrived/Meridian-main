namespace Meridian.QuantScript.Tests;

public sealed class StatisticsEngineTests
{
    private static double[] Ascending(int n) => Enumerable.Range(1, n).Select(i => i * 0.001).ToArray();

    [Fact] public void Mean_Returns_Average() => StatisticsEngine.Mean([1.0, 2.0, 3.0]).Should().BeApproximately(2.0, 1e-9);
    [Fact] public void StdDev_Returns_Positive_For_Varied_Data() => StatisticsEngine.StdDev(Ascending(20)).Should().BePositive();
    [Fact] public void Variance_Of_Constant_Array_Is_Zero() => StatisticsEngine.Variance([5.0, 5.0, 5.0]).Should().BeApproximately(0.0, 1e-9);
    [Fact] public void SharpeRatio_Returns_Positive_For_Ascending_Returns() => StatisticsEngine.SharpeRatio(Ascending(50)).Should().BePositive();
    [Fact] public void MaxDrawdown_Zero_For_Always_Rising() => StatisticsEngine.MaxDrawdown(Ascending(20)).Should().BeApproximately(0.0, 1e-9);
    [Fact] public void MaxDrawdown_Positive_For_Drop() => StatisticsEngine.MaxDrawdown([0.1, -0.2, 0.05]).Should().BePositive();
    [Fact] public void Correlation_Of_Identical_Series_Is_One() => StatisticsEngine.Correlation(Ascending(10), Ascending(10)).Should().BeApproximately(1.0, 1e-9);
    [Fact] public void CAGR_Positive_For_Rising_Returns() => StatisticsEngine.CAGR(Ascending(252), 1.0).Should().BePositive();
}
