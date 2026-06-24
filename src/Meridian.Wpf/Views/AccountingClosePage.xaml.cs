using System;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Views;

public partial class AccountingClosePage : Page
{
    public AccountingClosePage(AccountingCloseViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
