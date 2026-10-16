using SwitchHandover.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwitchHandover.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, ISnackbarService snackbarService)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
    }
}
