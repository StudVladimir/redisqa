using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using redisqa.Services;
using StackExchange.Redis;

namespace redisqa.ViewModels;

public class DataViewModel : BaseViewModel
{
    private int _selectedDb;
    private string? _schemaJson;
    private bool _hasSchema;
    private ObservableCollection<string> _tableNames = new();
    private string? _selectedTable;
    private bool _hasLoadedFile;
    private string? _loadedFileName;
    private List<Dictionary<string, string>>? _csvData;
    
    public int SelectedDb
    {
        get => _selectedDb;
        set
        {
            _selectedDb = value;
            OnPropertyChanged();
        }
    }
    
    public string? SchemaJson
    {
        get => _schemaJson;
        set
        {
            _schemaJson = value;
            OnPropertyChanged();
        }
    }
    
    public bool HasSchema
    {
        get => _hasSchema;
        set
        {
            _hasSchema = value;
            OnPropertyChanged();
        }
    }
    
    public ObservableCollection<string> TableNames
    {
        get => _tableNames;
        set
        {
            _tableNames = value;
            OnPropertyChanged();
        }
    }
    
    public string? SelectedTable
    {
        get => _selectedTable;
        set
        {
            _selectedTable = value;
            OnPropertyChanged();
            OnTableSelected();
        }
    }
    
    public bool HasLoadedFile
    {
        get => _hasLoadedFile;
        set
        {
            _hasLoadedFile = value;
            OnPropertyChanged();
        }
    }
    
    public string? LoadedFileName
    {
        get => _loadedFileName;
        set
        {
            _loadedFileName = value;
            OnPropertyChanged();
        }
    }
    
    public List<Dictionary<string, string>>? CsvData
    {
        get => _csvData;
        set
        {
            _csvData = value;
            OnPropertyChanged();
        }
    }

    public DataViewModel()
    {
        // Initialize with empty data
    }
    
    public void SetSchema(string? schemaJson)
    {
        SchemaJson = schemaJson;
        HasSchema = !string.IsNullOrEmpty(schemaJson);
        
        if (HasSchema)
        {
            ParseSchema();
        }
    }
    
    public void SetCsvData(string fileName, List<Dictionary<string, string>> data)
    {
        LoadedFileName = fileName;
        CsvData = data;
        HasLoadedFile = true;
        
        System.Diagnostics.Debug.WriteLine($"CSV loaded: {fileName}, Rows: {data.Count}");
    }
    
    public void ClearCsvData()
    {
        LoadedFileName = null;
        CsvData = null;
        HasLoadedFile = false;
    }
    
    private void ParseSchema()
    {
        try
        {
            if (string.IsNullOrEmpty(SchemaJson))
                return;
            
            var jsonDoc = JsonDocument.Parse(SchemaJson);
            var root = jsonDoc.RootElement;
            
            if (root.TryGetProperty("tables", out var tablesArray))
            {
                TableNames.Clear();
                
                foreach (var tableJson in tablesArray.EnumerateArray())
                {
                    if (tableJson.TryGetProperty("name", out var nameProperty))
                    {
                        var tableName = nameProperty.GetString();
                        if (!string.IsNullOrEmpty(tableName))
                        {
                            TableNames.Add(tableName);
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Schema loaded with {TableNames.Count} tables");
                
                // Автоматически выбираем первую таблицу если есть
                if (TableNames.Count > 0)
                {
                    SelectedTable = TableNames[0];
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing schema: {ex.Message}");
        }
    }
    
    private void OnTableSelected()
    {
        if (!string.IsNullOrEmpty(SelectedTable))
        {
            System.Diagnostics.Debug.WriteLine($"Selected table: {SelectedTable}");
            // Очищаем загруженный файл при смене таблицы
            ClearCsvData();
        }
    }
    
    public async Task<(bool success, string message)> InsertDataToRedis()
    {
        try
        {
            if (string.IsNullOrEmpty(SelectedTable) || CsvData == null || CsvData.Count == 0)
            {
                return (false, "No data to insert");
            }
            
            // Получаем Primary Key и индексированные колонки из схемы за один проход
            var (pkColumn, indexedColumns) = GetSchemaInfo();
            if (string.IsNullOrEmpty(pkColumn))
            {
                return (false, $"No Primary Key found for table {SelectedTable}");
            }
            
            System.Diagnostics.Debug.WriteLine($"Using Primary Key column: {pkColumn}");
            System.Diagnostics.Debug.WriteLine($"Indexed columns: {string.Join(", ", indexedColumns)}");
            
            // Получаем базу Redis
            var db = RedisConnectionService.Instance.GetDatabase(SelectedDb);
            if (db == null)
            {
                return (false, "Redis connection is not available");
            }
            
            int successCount = 0;
            int errorCount = 0;
            
            // Обрабатываем каждую строку
            foreach (var row in CsvData)
            {
                try
                {
                    // Проверяем наличие PK в строке (с учетом пробелов)
                    var pkValue = row.FirstOrDefault(kvp => 
                        kvp.Key.Trim().Equals(pkColumn.Trim(), StringComparison.OrdinalIgnoreCase)).Value;
                    
                    if (string.IsNullOrEmpty(pkValue))
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping row: Primary Key '{pkColumn}' not found or empty");
                        errorCount++;
                        continue;
                    }
                    
                    var pkValueTrimmed = pkValue.Trim();
                    
                    // Создаем ключ: TableName:PK_value
                    var redisKey = $"{SelectedTable}:{pkValueTrimmed}";
                    
                    // Подготавливаем HashEntry для всех полей кроме PK
                    var hashEntries = new List<HashEntry>();
                    foreach (var kvp in row)
                    {
                        var columnName = kvp.Key.Trim();
                        // Пропускаем Primary Key - он уже в ключе
                        if (columnName.Equals(pkColumn.Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;
                        
                        hashEntries.Add(new HashEntry(columnName, kvp.Value?.Trim() ?? ""));
                    }
                    
                    // Вставляем в Redis как хэш
                    if (hashEntries.Count > 0)
                    {
                        await db.HashSetAsync(redisKey, hashEntries.ToArray());
                        successCount++;
                        System.Diagnostics.Debug.WriteLine($"Inserted: {redisKey} with {hashEntries.Count} fields");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping row: No data fields besides Primary Key");
                        errorCount++;
                        continue;
                    }
                    
                    // Создаем индексы для indexed columns
                    foreach (var indexedColumn in indexedColumns)
                    {
                        var columnValue = row.FirstOrDefault(kvp => 
                            kvp.Key.Trim().Equals(indexedColumn.Trim(), StringComparison.OrdinalIgnoreCase)).Value;
                        
                        if (!string.IsNullOrEmpty(columnValue))
                        {
                            var indexKey = $"idx:{SelectedTable}:{indexedColumn}:{columnValue.Trim()}";
                            await db.SetAddAsync(indexKey, pkValueTrimmed);
                            System.Diagnostics.Debug.WriteLine($"Added to index: {indexKey} -> {pkValueTrimmed}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error inserting row: {ex.Message}");
                    errorCount++;
                }
            }
            
            var message = $"Inserted {successCount} rows successfully";
            if (errorCount > 0)
            {
                message += $", {errorCount} rows failed";
            }
            
            return (true, message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in InsertDataToRedis: {ex.Message}");
            return (false, $"Error: {ex.Message}");
        }
    }
    
    private (string? pkColumn, List<string> indexedColumns) GetSchemaInfo()
    {
        try
        {
            if (string.IsNullOrEmpty(SchemaJson) || string.IsNullOrEmpty(SelectedTable))
                return (null, new List<string>());
            
            var jsonDoc = JsonDocument.Parse(SchemaJson);
            var root = jsonDoc.RootElement;
            
            string? primaryKey = null;
            var indexedColumns = new List<string>();
            
            if (root.TryGetProperty("tables", out var tablesArray))
            {
                foreach (var table in tablesArray.EnumerateArray())
                {
                    if (table.TryGetProperty("name", out var nameProperty) &&
                        nameProperty.GetString() == SelectedTable)
                    {
                        // Нашли нужную таблицу, обрабатываем attributes
                        if (table.TryGetProperty("attributes", out var attributes))
                        {
                            foreach (var attr in attributes.EnumerateArray())
                            {
                                if (!attr.TryGetProperty("name", out var attrName))
                                    continue;
                                
                                var columnName = attrName.GetString();
                                if (string.IsNullOrEmpty(columnName))
                                    continue;
                                
                                // Проверяем на PK
                                if (attr.TryGetProperty("pk", out var pkProp) && pkProp.GetBoolean())
                                {
                                    primaryKey = columnName;
                                }
                                
                                // Проверяем на idx
                                if (attr.TryGetProperty("idx", out var idxProp) && idxProp.GetBoolean())
                                {
                                    indexedColumns.Add(columnName);
                                }
                            }
                        }
                        
                        break; // Таблица найдена, выходим
                    }
                }
            }
            
            return (primaryKey, indexedColumns);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting schema info: {ex.Message}");
            return (null, new List<string>());
        }
    }
}
