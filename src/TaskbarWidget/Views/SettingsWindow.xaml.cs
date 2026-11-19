using System.Windows;
using TaskbarWidget.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TaskbarWidget.Views;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}
