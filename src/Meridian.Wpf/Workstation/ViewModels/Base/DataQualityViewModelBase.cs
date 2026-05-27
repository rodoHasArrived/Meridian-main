namespace Meridian.Wpf.Workstation.ViewModels.Base;

public abstract class DataQualityViewModelBase : WorkspaceViewModelBase
{
    private string _qualitySummary = string.Empty;

    public string QualitySummary
    {
        get => _qualitySummary;
        protected set => SetProperty(ref _qualitySummary, value);
    }
}
