using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OperationalExceptionTriageViewModelTests
{
    [Fact]
    public void Constructor_loads_reconciliation_and_provider_quality_exceptions_first()
    {
        var viewModel = new OperationalExceptionTriageViewModel(new OperationalExceptionTriageService());

        viewModel.Exceptions.Should().NotBeEmpty();
        viewModel.Exceptions.Should().Contain(exception => exception.SourceCategory == "Reconciliation");
        viewModel.Exceptions.Should().Contain(exception => exception.SourceCategory == "Provider/Data quality");
        viewModel.SelectedException.Should().NotBeNull();
        viewModel.SelectedAuditHistory.Should().BeInDescendingOrder(entry => entry.Timestamp);
    }

    [Fact]
    public void ApplyDeepLinkFilter_limits_queue_to_requested_workspace_and_exception_type()
    {
        var viewModel = new OperationalExceptionTriageViewModel(new OperationalExceptionTriageService());

        viewModel.ApplyDeepLinkFilter("Data", "Provider/Data quality");

        viewModel.Exceptions.Should().OnlyContain(exception =>
            exception.SourceWorkspace == "Data" &&
            exception.SourceCategory == "Provider/Data quality");
        viewModel.QueueSummary.Should().Contain("operational exceptions in scope");
    }

    [Fact]
    public void Filter_preserves_selection_when_selected_exception_stays_visible()
    {
        var viewModel = new OperationalExceptionTriageViewModel(new OperationalExceptionTriageService());
        viewModel.SelectedException = viewModel.Exceptions.Single(exception => exception.Id == "DATA-101");

        viewModel.ApplyDeepLinkFilter("Data", "Provider/Data quality");

        viewModel.SelectedException.Should().NotBeNull();
        viewModel.SelectedException!.Id.Should().Be("DATA-101");
    }
}
