using Avalonia.Controls;
using Avalonia.Media;
using redisqa.ViewModels;
using redisqa.Views;

namespace redisqa;

public partial class MainWindow : Window
{
    private MainWindowViewModel _viewModel = new MainWindowViewModel();
    private Button? _selectedDbButton;
    private Button? _selectedTabButton;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
        
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