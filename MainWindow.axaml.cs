using Avalonia.Controls;
using redisqa.ViewModels;
using redisqa.Views;

namespace redisqa;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = new MainWindowViewModel();
    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
    }

    private void BtnHome_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
       _viewModel.NavigateToHome();
    }

    private void BtnRedis_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.NavigateToSecond();
    }
}