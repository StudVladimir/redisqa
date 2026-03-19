using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using redisqa.ViewModels;
using redisqa.Views;
using redisqa.Services;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace redisqa;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = new MainWindowViewModel();
    private Button? _selectedDbButton;
    private Button? _selectedTabButton;
    private TextBlock? _connectionStatusText;
    
    // Enum для отслеживания текущего активного View
    private enum ActiveView
    {
        None,
        Home,
        Schema,
        Queries,
        Data
    }
    
    private ActiveView _currentActiveView = ActiveView.None;

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
        
        // Устанавливаем db_0 по умолчанию
        _viewModel.SelectedDb = 0;
        
        // Выбираем Schema по умолчанию
        _currentActiveView = ActiveView.Schema;
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
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedDb))
        {
            // Автоматически перезагружаем текущий View при смене базы данных
            ReloadCurrentView();
        }
    }
    
    private void ReloadCurrentView()
    {
        // Перезагружаем текущий активный View
        switch (_currentActiveView)
        {
            case ActiveView.Schema:
                _viewModel.NavigateToSchema();
                break;
            case ActiveView.Data:
                _viewModel.NavigateToData();
                break;
            case ActiveView.Queries:
                _viewModel.NavigateToQueries();
                break;
            case ActiveView.Home:
            case ActiveView.None:
                // Home и None не зависят от выбранной БД
                break;
        }
    }
    
    private void UpdateConnectionStatusColor()
    {
        if (_connectionStatusText != null)
        {
            _connectionStatusText.Foreground = _viewModel.IsConnected 
                ? new SolidColorBrush(Color.Parse("#28B57D"))
                : new SolidColorBrush(Color.Parse("#e74c3c"));
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
       _currentActiveView = ActiveView.Home;
       _viewModel.NavigateToHome();
    }

    private void BtnSchema_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentActiveView = ActiveView.Schema;
        _viewModel.NavigateToSchema();
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    private void BtnQueries_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentActiveView = ActiveView.Queries;
        _viewModel.NavigateToQueries();
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    private void BtnData_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentActiveView = ActiveView.Data;
        _viewModel.NavigateToData();
        if (sender is Button button)
        {
            HighlightTab(button);
        }
    }

    // Обработчики для нового выбора базы данных
    private void DbDisplay_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Скрываем отображение, показываем поле ввода
        var dbDisplayBorder = this.FindControl<Border>("DbDisplayBorder");
        var dbInputBorder = this.FindControl<Border>("DbInputBorder");
        var dbInputTextBox = this.FindControl<TextBox>("DbInputTextBox");
        
        if (dbDisplayBorder != null && dbInputBorder != null && dbInputTextBox != null)
        {
            dbDisplayBorder.IsVisible = false;
            dbInputBorder.IsVisible = true;
            
            // Устанавливаем текущее значение в поле ввода
            dbInputTextBox.Text = _viewModel.SelectedDb?.ToString() ?? "0";
            
            // Фокусируемся на поле ввода и выделяем весь текст
            dbInputTextBox.Focus();
            dbInputTextBox.SelectAll();
        }
    }
    
    private void DbInput_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            ConfirmDbInput();
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            CancelDbInput();
        }
    }
    
    private void DbInputConfirm_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ConfirmDbInput();
    }
    
    private async void ConfirmDbInput()
    {
        var dbInputTextBox = this.FindControl<TextBox>("DbInputTextBox");
        
        if (dbInputTextBox == null || string.IsNullOrWhiteSpace(dbInputTextBox.Text))
        {
            await ShowErrorDialog("Invalid Input", "Please enter a database number.");
            return;
        }
        
        // Пытаемся распарсить введенное значение
        if (!int.TryParse(dbInputTextBox.Text, out int dbNumber))
        {
            await ShowErrorDialog("Invalid Input", "Database number must be a valid integer.\n\nPlease enter a number between 0 and 15.");
            return;
        }
        
        // Проверяем диапазон
        if (dbNumber < 0 || dbNumber > 15)
        {
            await ShowErrorDialog("Out of Range", $"Database number must be between 0 and 15.\n\nYou entered: {dbNumber}");
            return;
        }
        
        // Устанавливаем новое значение
        _viewModel.SelectedDb = dbNumber;
        
        // Возвращаемся к режиму отображения
        CancelDbInput();
    }
    
    private void CancelDbInput()
    {
        var dbDisplayBorder = this.FindControl<Border>("DbDisplayBorder");
        var dbInputBorder = this.FindControl<Border>("DbInputBorder");
        
        if (dbDisplayBorder != null && dbInputBorder != null)
        {
            dbInputBorder.IsVisible = false;
            dbDisplayBorder.IsVisible = true;
        }
    }
    
    private async Task ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly
        };
        
        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };
        
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        });
        
        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(30, 8),
            Background = new SolidColorBrush(Color.Parse("#3498db")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        
        okButton.Click += (s, e) => dialog.Close();
        content.Children.Add(okButton);
        
        dialog.Content = content;
        
        await dialog.ShowDialog(this);
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
        // Скрываем все индикаторы
        var schemaIndicator = this.FindControl<Border>("SchemaIndicator");
        var queriesIndicator = this.FindControl<Border>("QueriesIndicator");
        var dataIndicator = this.FindControl<Border>("DataIndicator");
        
        if (schemaIndicator != null) schemaIndicator.IsVisible = false;
        if (queriesIndicator != null) queriesIndicator.IsVisible = false;
        if (dataIndicator != null) dataIndicator.IsVisible = false;
        
        // Удаляем класс active у всех кнопок
        var btnSchema = this.FindControl<Button>("BtnSchema");
        var btnQueries = this.FindControl<Button>("BtnQueries");
        var btnData = this.FindControl<Button>("BtnData");
        
        if (btnSchema != null) btnSchema.Classes.Remove("active");
        if (btnQueries != null) btnQueries.Classes.Remove("active");
        if (btnData != null) btnData.Classes.Remove("active");
        
        // Активируем нужную кнопку и индикатор
        button.Classes.Add("active");
        
        if (button.Name == "BtnSchema" && schemaIndicator != null)
        {
            schemaIndicator.IsVisible = true;
        }
        else if (button.Name == "BtnQueries" && queriesIndicator != null)
        {
            queriesIndicator.IsVisible = true;
        }
        else if (button.Name == "BtnData" && dataIndicator != null)
        {
            dataIndicator.IsVisible = true;
        }
        
        _selectedTabButton = button;
    }
    
    private void TabButton_PointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is not Button button) return;
        
        // Получаем соответствующий hover индикатор
        Border? hoverIndicator = button.Name switch
        {
            "BtnSchema" => this.FindControl<Border>("SchemaHoverIndicator"),
            "BtnQueries" => this.FindControl<Border>("QueriesHoverIndicator"),
            "BtnData" => this.FindControl<Border>("DataHoverIndicator"),
            _ => null
        };
        
        // Показываем hover индикатор, только если это не активная кнопка
        if (hoverIndicator != null && !button.Classes.Contains("active"))
        {
            hoverIndicator.IsVisible = true;
        }
    }
    
    private void TabButton_PointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is not Button button) return;
        
        // Получаем соответствующий hover индикатор
        Border? hoverIndicator = button.Name switch
        {
            "BtnSchema" => this.FindControl<Border>("SchemaHoverIndicator"),
            "BtnQueries" => this.FindControl<Border>("QueriesHoverIndicator"),
            "BtnData" => this.FindControl<Border>("DataHoverIndicator"),
            _ => null
        };
        
        // Скрываем hover индикатор
        if (hoverIndicator != null)
        {
            hoverIndicator.IsVisible = false;
        }
    }

    private void BtnDb0_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // TODO: Implement data navigation
    }
}