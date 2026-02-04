using System.Collections.ObjectModel;
using redisqa.Models;

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

    public void RemoveAttribute(TableModel table, AttributeModel attribute)
    {
        table.Attributes.Remove(attribute);
    }
}
