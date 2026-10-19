using System.Windows;
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

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                ViewModel.ProcessSelectedFiles(files);
            }
        }
    }
}
