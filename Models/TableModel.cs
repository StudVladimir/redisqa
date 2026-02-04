using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace redisqa.Models;

public class TableModel : INotifyPropertyChanged
{
    private string _name = "New Table";
    private double _x;
    private double _y;

    public string Name 
    { 
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }
    
    public ObservableCollection<AttributeModel> Attributes { get; set; } = new();
    
    // Position on canvas
    public double X 
    { 
        get => _x;
        set
        {
            _x = value;
            OnPropertyChanged();
        }
    }
    
    public double Y 
    { 
        get => _y;
        set
        {
            _y = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
