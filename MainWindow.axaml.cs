using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using redisqa.ViewModels;
using redisqa.Views;
using redisqa.Services;
using System;
using System.ComponentModel;

namespace redisqa;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = new MainWindowViewModel();
    private Button? _selectedDbButton;
    private Button? _selectedTabButton;
    private TextBlock? _connectionStatusText;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
        
        // Получить ссылку на текстовый блок статуса
        _connectionStatusText = this.FindControl<TextBlock>("ConnectionStatusText");
        
        // Подписаться на изменения свойства IsConnected
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Установить начальный цвет
        UpdateConnectionStatusColor();
        
        // Выбираем db_0 по умолчанию
        var btnDb0 = this.FindControl<Button>("btnDb0");
        if (btnDb0 != null)
        {
            HighlightButton(btnDb0);
        }
        
        // Выбираем Schema по умолчанию
        var btnSchema = this.FindControl<Button>("BtnSchema");
        if (btnSchema != null)
        {
            HighlightTab(btnSchema);
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
        {
            UpdateConnectionStatusColor();
        }
    }
    
    private void UpdateConnectionStatusColor()
    {
        if (_connectionStatusText != null)
        {
            _connectionStatusText.Foreground = _viewModel.IsConnected 
                ? new SolidColorBrush(Color.Parse("#28B57D"))  // Зеленый
                : new SolidColorBrush(Color.Parse("#e74c3c"));  // Красный
        }
    }
    
    private async void BtnRefresh_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _viewModel.CheckConnectionAsync();
    }
    
    private async void BtnDisconnect_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Отключиться от Redis
        await RedisConnectionService.Instance.DisconnectAsync();
        
        // Закрыть MainWindow и открыть ConnectionWindow
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var connectionWindow = new ConnectionWindow();
            desktop.MainWindow = connectionWindow;
            connectionWindow.Show();
            this.Close();
        }
    }

    private void BtnHome_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
       _viewModel.NavigateToHome();
    }

    private void BtnSchema_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.NavigateToSchema();
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    private void BtnQueries_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: Implement queries navigation
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    private void BtnData_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: Implement data navigation
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    // Общий обработчик для всех кнопок выбора базы данных
    private void BtnDb_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tagValue)
        {
            if (int.TryParse(tagValue, out int dbNumber))
            {
                _viewModel.SelectedDb = dbNumber;
                HighlightButton(button);
            }
        }
    }

    private void HighlightButton(Button button)
    {
        // Сбрасываем предыдущую выбранную кнопку
        if (_selectedDbButton != null)
        {
            _selectedDbButton.Background = new SolidColorBrush(Color.Parse("#34495e80"));
            _selectedDbButton.FontWeight = FontWeight.Normal;
        }

        // Подсвечиваем новую кнопку
        button.Background = new SolidColorBrush(Color.Parse("#3498db"));
        button.FontWeight = FontWeight.Bold;
        _selectedDbButton = button;
    }
    
    private void HighlightTab(Button button)
    {
        // Сбрасываем предыдущую выбранную вкладку
        if (_selectedTabButton != null)
        {
            _selectedTabButton.Classes.Remove("active");
        }

        // Активируем новую вкладку
        button.Classes.Add("active");
        _selectedTabButton = button;
    }

    private void BtnDb0_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: Implement data navigation
    }
}