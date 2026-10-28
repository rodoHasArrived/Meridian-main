using System;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to edit close plans, dependencies, sign-offs, adjustments, evidence, blockers, and locks.
/// </summary>
public partial class AccountingClosePage : Page
{
    public AccountingClosePage(AccountingCloseViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
