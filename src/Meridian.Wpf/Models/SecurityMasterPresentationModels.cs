namespace Meridian.Wpf.Models;

public sealed record SecurityMasterPresentationField(
    string Label,
    string Value);

public sealed record SecurityMasterPrintSectionItem(
    string Title,
    string Description,
    string Badge);

public sealed record SecurityMasterChecklistItem(
    string Label,
    string Owner,
    string Status);

public sealed record SecurityMasterEvidenceItem(
    string Title,
    string Detail,
    string Destination);
