using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.ComponentModel;
using redisqa.ViewModels;

namespace redisqa;

public partial class ConnectionWindow : Window
{
    private ConnectionWindowViewModel _viewModel;
    private Border? _statusBorder;
    
    public ConnectionWindow()
    {
        InitializeComponent();
        _viewModel = new ConnectionWindowViewModel();
        DataContext = _viewModel;
        
        // Subscribe to ViewModel property changes
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Get status border reference
        _statusBorder = this.FindControl<Border>("StatusBorder");
        
        // Subscribe to connect button
        var btnConnect = this.FindControl<Button>("BtnConnect");
        if (btnConnect != null)
        {
            btnConnect.Click += BtnConnect_Click;
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionWindowViewModel.HasError) && _statusBorder != null)
        {
            // Изменить цвет фона в зависимости от ошибки
            _statusBorder.Background = _viewModel.HasError 
                ? new SolidColorBrush(Color.Parse("#e74c3c"))  // Красный для ошибки
                : new SolidColorBrush(Color.Parse("#28B57D")); // Зеленый для успеха
        }
    }
    
    private async void BtnConnect_Click(object? sender, RoutedEventArgs e)
    {
        var success = await _viewModel.ConnectAsync();
        
        if (success)
        {
            // Open MainWindow and close ConnectionWindow
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                this.Close();
            }
        }
    }
}
