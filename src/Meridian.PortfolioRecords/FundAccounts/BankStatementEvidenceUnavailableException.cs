namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Financial calculations cannot choose between competing retained statement authorities.</summary>
public sealed class BankStatementEvidenceUnavailableException : InvalidOperationException
{
    public BankStatementEvidenceUnavailableException()
        : base("Cash-flow evidence is unavailable because competing bank statement batches have the same header date and revision.")
    {
    }
}
