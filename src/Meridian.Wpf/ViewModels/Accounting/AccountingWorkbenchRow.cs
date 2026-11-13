namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Presentation-only evidence row shared by Accounting inspection panels.</summary>
public sealed record AccountingWorkbenchRow(string Name, string Status, string Detail, string Evidence, string Key = "");
