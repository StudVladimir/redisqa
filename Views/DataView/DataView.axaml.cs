using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using redisqa.Services;
using redisqa.ViewModels;

namespace redisqa.Views.DataView;

public partial class DataView : UserControl
{
    private DataViewModel _viewModel;
    private Border? _dropZone;

    public DataView()
    {
        InitializeComponent();
        
        _viewModel = new DataViewModel();
        DataContext = _viewModel;
        
        // Получаем ссылку на Drop Zone
        _dropZone = this.FindControl<Border>("DropZone");
        
        // Подписываемся на события drag and drop
        if (_dropZone != null)
        {
            _dropZone.AddHandler(DragDrop.DropEvent, Drop);
            _dropZone.AddHandler(DragDrop.DragOverEvent, DragOver);
            _dropZone.AddHandler(DragDrop.DragEnterEvent, DragEnter);
            _dropZone.AddHandler(DragDrop.DragLeaveEvent, DragLeave);
        }
        
        // Загружаем схему из Redis асинхронно после всех инициализаций
        _ = LoadSchemaFromRedisAsync();
    }
    
    private async Task LoadSchemaFromRedisAsync()
    {
        try
        {
            // Получаем selectedDb из контекста для ViewModel
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow?.DataContext is MainWindowViewModel mainViewModel)
                {
                    _viewModel.SelectedDb = mainViewModel.SelectedDb ?? 0;
                }
            }
            
            // Используем сервис для получения схемы с автоматическим определением selectedDb
            var schemaJson = await GetSchemaFromRedis.Instance.GetSchemaForCurrentContextAsync();
            
            if (!string.IsNullOrEmpty(schemaJson))
            {
                // Если схема найдена, устанавливаем её в ViewModel
                _viewModel.SetSchema(schemaJson);
                System.Diagnostics.Debug.WriteLine("Schema loaded successfully for DataView");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No schema found in Redis for DataView");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading schema from Redis in DataView: {ex.Message}");
        }
    }
    
    private async void BtnSelectFile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select CSV File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("CSV Files")
                    {
                        Patterns = new[] { "*.csv" }
                    }
                }
            });
            
            if (files.Count > 0)
            {
                var file = files[0];
                var filePath = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(filePath))
                {
                    await ProcessCsvFileByPath(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error selecting file: {ex.Message}");
        }
    }
    
    private async void BtnInsertData_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Показываем индикатор загрузки (можно добавить в будущем)
            System.Diagnostics.Debug.WriteLine("Starting data insertion...");
            
            var (success, message) = await _viewModel.InsertDataToRedis();
            
            if (success)
            {
                System.Diagnostics.Debug.WriteLine($"Success: {message}");
                // TODO: Показать уведомление пользователю об успешной вставке
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Error: {message}");
                // TODO: Показать уведомление пользователю об ошибке
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error inserting data: {ex.Message}");
        }
    }
    
    private void DragOver(object? sender, DragEventArgs e)
    {
        // Проверяем что перетаскиваются файлы
        #pragma warning disable CS0618 // Type or member is obsolete
        var files = e.Data.GetFiles();
        #pragma warning restore CS0618 // Type or member is obsolete
        if (files != null && files.Any())
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        
        e.Handled = true;
    }
    
    private void DragEnter(object? sender, DragEventArgs e)
    {
        // Подсвечиваем зону при наведении
        #pragma warning disable CS0618 // Type or member is obsolete
        var files = e.Data.GetFiles();
        #pragma warning restore CS0618 // Type or member is obsolete
        if (_dropZone != null && files != null && files.Any())
        {
            _dropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2ecc71"));
            _dropZone.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2c3e50"));
        }
    }
    
    private void DragLeave(object? sender, DragEventArgs e)
    {
        // Убираем подсветку
        if (_dropZone != null)
        {
            _dropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3498db"));
            _dropZone.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34495e"));
        }
    }
    
    private async void Drop(object? sender, DragEventArgs e)
    {
        // Убираем подсветку
        if (_dropZone != null)
        {
            _dropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3498db"));
            _dropZone.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34495e"));
        }
        
        try
        {
            #pragma warning disable CS0618 // Type or member is obsolete
            var files = e.Data.GetFiles();
            #pragma warning restore CS0618 // Type or member is obsolete
            if (files != null && files.Any())
            {
                var file = files.First();
                var filePath = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(filePath))
                {
                    await ProcessCsvFileByPath(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error dropping file: {ex.Message}");
        }
    }
    
    private async Task ProcessCsvFileByPath(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            
            // Проверяем расширение файла
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine("Only CSV files are supported");
                return;
            }
            
            // Читаем файл
            using var reader = new StreamReader(filePath);
            
            var csvData = new List<Dictionary<string, string>>();
            string? headerLine = await reader.ReadLineAsync();
            
            if (string.IsNullOrEmpty(headerLine))
            {
                System.Diagnostics.Debug.WriteLine("CSV file is empty");
                return;
            }
            
            // Парсим заголовки
            var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();
            
            // Читаем данные
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var values = line.Split(',').Select(v => v.Trim()).ToArray();
                var row = new Dictionary<string, string>();
                
                for (int i = 0; i < headers.Length && i < values.Length; i++)
                {
                    row[headers[i]] = values[i];
                }
                
                csvData.Add(row);
            }
            
            // Сохраняем в ViewModel
            _viewModel.SetCsvData(fileName, csvData);
            
            System.Diagnostics.Debug.WriteLine($"CSV file processed: {fileName}, Rows: {csvData.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing CSV file: {ex.Message}");
        }
    }
}
