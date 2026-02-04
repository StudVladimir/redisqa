using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using redisqa.Models;
using redisqa.ViewModels;

namespace redisqa.Views;

public partial class SchemaView : UserControl
{
    private SchemaViewModel _viewModel;
    private Canvas? _canvas;
    private bool _isCreatingFKLink = false;
    private TableModel? _fkSourceTable;
    private AttributeModel? _fkSourceAttribute;
    private Line? _fkLinkLine;

    public SchemaView()
    {
        InitializeComponent();
        
        _viewModel = new SchemaViewModel();
        DataContext = _viewModel;
        
        // Get reference to canvas
        _canvas = this.FindControl<Canvas>("SchemaCanvas");
        
        // Subscribe to canvas pointer events for FK link creation
        if (_canvas != null)
        {
            _canvas.PointerMoved += Canvas_PointerMoved;
            _canvas.PointerPressed += Canvas_PointerPressed;
        }
        
        // Subscribe to collection changes
        _viewModel.Tables.CollectionChanged += Tables_CollectionChanged;
        
        // Add existing tables
        foreach (var table in _viewModel.Tables)
        {
            AddTableToCanvas(table);
        }
        
        // Wire up button handlers
        var btnAddTable = this.FindControl<Button>("BtnAddTable");
        if (btnAddTable != null)
        {
            btnAddTable.Click += BtnAddTable_Click;
        }
        
        var btnClear = this.FindControl<Button>("BtnClear");
        if (btnClear != null)
        {
            btnClear.Click += BtnClear_Click;
        }
    }

    private void Tables_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_canvas == null) return;

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is Models.TableModel table)
                {
                    AddTableToCanvas(table);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is Models.TableModel table)
                {
                    RemoveTableFromCanvas(table);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _canvas.Children.Clear();
        }
    }

    private void AddTableToCanvas(Models.TableModel table)
    {
        if (_canvas == null) return;

        var tableCard = new TableCard
        {
            DataContext = table
        };
        
        // Subscribe to FK link requests
        tableCard.FKLinkRequested += TableCard_FKLinkRequested;

        _canvas.Children.Add(tableCard);
    }

    private void RemoveTableFromCanvas(Models.TableModel table)
    {
        if (_canvas == null) return;

        var cardToRemove = _canvas.Children
            .OfType<TableCard>()
            .FirstOrDefault(card => card.DataContext == table);

        if (cardToRemove != null)
        {
            _canvas.Children.Remove(cardToRemove);
        }
    }

    private void BtnAddTable_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.AddTable();
    }

    private void BtnClear_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.Tables.Clear();
    }
    
    private void TableCard_FKLinkRequested(object? sender, FKLinkEventArgs e)
    {
        // Start FK link creation mode
        _isCreatingFKLink = true;
        _fkSourceTable = e.SourceTable;
        _fkSourceAttribute = e.SourceAttribute;
        
        // Create line for visual feedback
        if (_canvas != null)
        {
            _fkLinkLine = new Line
            {
                Stroke = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 2,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 5, 3 }
            };
            
            // Set start point at source table center
            var sourceCard = sender as TableCard;
            if (sourceCard != null)
            {
                var startX = _fkSourceTable.X + sourceCard.Bounds.Width / 2;
                var startY = _fkSourceTable.Y + sourceCard.Bounds.Height / 2;
                _fkLinkLine.StartPoint = new Point(startX, startY);
                _fkLinkLine.EndPoint = new Point(startX, startY);
            }
            
            _canvas.Children.Add(_fkLinkLine);
        }
    }
    
    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isCreatingFKLink && _fkLinkLine != null && _canvas != null)
        {
            var position = e.GetPosition(_canvas);
            _fkLinkLine.EndPoint = position;
        }
    }
    
    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isCreatingFKLink) return;
        
        // Check if clicked on a table
        var position = e.GetPosition(_canvas);
        var clickedTable = FindTableAtPosition(position);
        
        if (clickedTable != null && _fkSourceTable != null && _fkSourceAttribute != null)
        {
            // Don't allow linking to the same table
            if (clickedTable != _fkSourceTable)
            {
                // Create reference attribute in target table
                var refAttributeName = $"{_fkSourceTable.Name}_{_fkSourceAttribute.Name}";
                var refAttribute = new AttributeModel
                {
                    Name = refAttributeName,
                    IsIndex = true,
                    IsForeignKey = true,
                    ForeignKeyReferences = new System.Collections.Generic.List<ForeignKeyReference>
                    {
                        new ForeignKeyReference
                        {
                            Condition = "references",
                            ReferenceTable = _fkSourceTable.Name,
                            ReferenceAttribute = _fkSourceAttribute.Name
                        }
                    }
                };
                
                clickedTable.Attributes.Add(refAttribute);
            }
        }
        
        // Clean up
        if (_fkLinkLine != null && _canvas != null)
        {
            _canvas.Children.Remove(_fkLinkLine);
        }
        
        _isCreatingFKLink = false;
        _fkSourceTable = null;
        _fkSourceAttribute = null;
        _fkLinkLine = null;
    }
    
    private TableModel? FindTableAtPosition(Point position)
    {
        if (_canvas == null) return null;
        
        foreach (var child in _canvas.Children)
        {
            if (child is TableCard tableCard && tableCard.DataContext is TableModel table)
            {
                var bounds = new Rect(table.X, table.Y, tableCard.Bounds.Width, tableCard.Bounds.Height);
                if (bounds.Contains(position))
                {
                    return table;
                }
            }
        }
        
        return null;
    }
}
