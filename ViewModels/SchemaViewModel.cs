using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using redisqa.Models;
using redisqa.Services;

namespace redisqa.ViewModels;

public class SchemaViewModel : BaseViewModel
{
    private ObservableCollection<TableModel> _tables = new();
    public ObservableCollection<TableModel> Tables
    {
        get => _tables;
        set
        {
            _tables = value;
            OnPropertyChanged();
        }
    }
    
    private ObservableCollection<FKLinkModel> _fkLinks = new();
    public ObservableCollection<FKLinkModel> FKLinks
    {
        get => _fkLinks;
        set
        {
            _fkLinks = value;
            OnPropertyChanged();
        }
    }

    private TableModel? _selectedTable;
    public TableModel? SelectedTable
    {
        get => _selectedTable;
        set
        {
            _selectedTable = value;
            OnPropertyChanged();
        }
    }
    
    private int _selectedDb;
    public int SelectedDb
    {
        get => _selectedDb;
        set
        {
            _selectedDb = value;
            OnPropertyChanged();
        }
    }

    public SchemaViewModel()
    {
        // Initialize with empty collection
    }

    public void AddTable()
    {
        var newTable = new TableModel
        {
            Name = $"Table_{Tables.Count + 1}",
            X = 150 + (Tables.Count * 30), // Offset each new table
            Y = 150 + (Tables.Count * 30)
        };
        
        // Add default attributes
        newTable.Attributes.Add(new AttributeModel 
        { 
            Name = "id", 
            IsPrimaryKey = true, 
            IsIndex = true 
        });
        
        Tables.Add(newTable);
        SelectedTable = newTable;
    }

    public void RemoveTable(TableModel table)
    {
        Tables.Remove(table);
        if (SelectedTable == table)
        {
            SelectedTable = null;
        }
    }

    public void AddAttribute(TableModel table)
    {
        table.Attributes.Add(new AttributeModel 
        { 
            Name = $"attribute_{table.Attributes.Count + 1}" 
        });
    }
    
    public async Task SaveSchemaAsync()
    {
        var schemaDir = Path.Combine(Directory.GetCurrentDirectory(), "ER-schema");
        Directory.CreateDirectory(schemaDir);
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"schema_{timestamp}.json";
        var filePath = Path.Combine(schemaDir, fileName);
        
        var schemaJson = new
        {
            tables = Tables.Select(t => new
            {
                name = t.Name,
                attributes = t.Attributes.Select(a =>
                {
                    object fkValue;
                    if (a.IsForeignKey && a.ForeignKeyReferences != null && a.ForeignKeyReferences.Count > 0)
                    {
                        fkValue = a.ForeignKeyReferences.Select(fk => new
                        {
                            condition = fk.Condition,
                            reference_table = fk.ReferenceTable,
                            reference_attribute = fk.ReferenceAttribute
                        }).ToList();
                    }
                    else
                    {
                        fkValue = false;
                    }
                    
                    return new
                    {
                        name = a.Name,
                        PK = a.IsPrimaryKey,
                        IDX = a.IsIndex,
                        FK = fkValue
                    };
                }).ToList()
            }).ToList()
        };
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var json = JsonSerializer.Serialize(schemaJson, options);
        await File.WriteAllTextAsync(filePath, json);
        
        System.Diagnostics.Debug.WriteLine($"Schema saved to: {filePath}");
    }
    
    public async Task SaveSchemaToRedisAsync()
    {
        try
        {
            // Проверяем подключение к Redis
            if (!RedisConnectionService.Instance.IsConnected)
            {
                throw new Exception("Not connected to Redis");
            }
            
            // Получаем базу данных с выбранным индексом
            var db = RedisConnectionService.Instance.GetDatabase(SelectedDb);
            if (db == null)
            {
                throw new Exception($"Failed to get database {SelectedDb}");
            }
            
            // Формируем тот же JSON что и в SaveSchemaAsync
            var schemaJson = new
            {
                tables = Tables.Select(t => new
                {
                    name = t.Name,
                    attributes = t.Attributes.Select(a =>
                    {
                        object fkValue;
                        if (a.IsForeignKey && a.ForeignKeyReferences != null && a.ForeignKeyReferences.Count > 0)
                        {
                            fkValue = a.ForeignKeyReferences.Select(fk => new
                            {
                                condition = fk.Condition,
                                reference_table = fk.ReferenceTable,
                                reference_attribute = fk.ReferenceAttribute
                            }).ToList();
                        }
                        else
                        {
                            fkValue = false;
                        }
                        
                        return new
                        {
                            name = a.Name,
                            PK = a.IsPrimaryKey,
                            IDX = a.IsIndex,
                            FK = fkValue
                        };
                    }).ToList()
                }).ToList()
            };
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var jsonString = JsonSerializer.Serialize(schemaJson, options);
            
            var redisKey = $"ERD-SCHEMA";

            
            // Сохраняем в Redis как строку
            await db.StringSetAsync(redisKey, jsonString);
            
            System.Diagnostics.Debug.WriteLine($"Schema saved to Redis with key: {redisKey} in database {SelectedDb}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving schema to Redis: {ex.Message}");
            throw;
        }
    }

    public void RemoveAttribute(TableModel table, AttributeModel attribute)
    {
        table.Attributes.Remove(attribute);
    }
}
