using System.Collections.Generic;
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

namespace redisqa.Views.SchemaView;

public partial class SchemaView : UserControl
{
    private SchemaViewModel _viewModel;
    private Canvas? _canvas;
    private bool _isCreatingFKLink = false;
    private TableModel? _fkSourceTable;
    private AttributeModel? _fkSourceAttribute;
    private Line? _fkLinkLine;
    private Dictionary<FKLinkModel, Line> _fkLinkLines = new();

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
        
        // Subscribe to FK links collection changes
        _viewModel.FKLinks.CollectionChanged += FKLinks_CollectionChanged;
        
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
        
        var btnSave = this.FindControl<Button>("BtnSave");
        if (btnSave != null)
        {
            btnSave.Click += BtnSave_Click;
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
        
        // Subscribe to table delete requests
        tableCard.TableDeleteRequested += TableCard_TableDeleteRequested;
        
        // Subscribe to attribute delete requests
        tableCard.AttributeDeleteRequested += TableCard_AttributeDeleteRequested;
        
        // Subscribe to table position changes to update FK lines
        table.PropertyChanged += Table_PropertyChanged;

        _canvas.Children.Add(tableCard);
    }
    
    private void TableCard_AttributeDeleteRequested(object? sender, AttributeDeleteEventArgs e)
    {
        // Remove all FK links where this attribute is involved
        var linksToRemove = _viewModel.FKLinks
            .Where(link => 
                (link.SourceTable == e.Table && link.SourceAttribute == e.Attribute) ||
                (link.TargetTable == e.Table && link.TargetAttribute == e.Attribute))
            .ToList();
        
        foreach (var link in linksToRemove)
        {
            _viewModel.FKLinks.Remove(link);
        }
    }
    
    private void TableCard_TableDeleteRequested(object? sender, TableModel table)
    {
        // Remove all FK links related to this table
        var linksToRemove = _viewModel.FKLinks
            .Where(link => link.SourceTable == table || link.TargetTable == table)
            .ToList();
        
        foreach (var link in linksToRemove)
        {
            _viewModel.FKLinks.Remove(link);
        }
        
        // Remove the table
        _viewModel.Tables.Remove(table);
    }
    
    private void Table_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TableModel table && (e.PropertyName == nameof(TableModel.X) || e.PropertyName == nameof(TableModel.Y)))
        {
            UpdateFKLines();
        }
    }
    
    private void FKLinks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_canvas == null) return;

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is FKLinkModel fkLink)
                {
                    AddFKLinkLine(fkLink);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is FKLinkModel fkLink)
                {
                    RemoveFKLinkLine(fkLink);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var line in _fkLinkLines.Values)
            {
                _canvas.Children.Remove(line);
            }
            _fkLinkLines.Clear();
        }
    }
    
    private void AddFKLinkLine(FKLinkModel fkLink)
    {
        if (_canvas == null) return;

        var line = new Line
        {
            Stroke = new SolidColorBrush(Color.Parse("#3d5af1")),
            StrokeThickness = 2
        };
        
        _fkLinkLines[fkLink] = line;
        _canvas.Children.Insert(0, line); // Add at the beginning so it's behind tables
        
        UpdateFKLineLine(fkLink, line);
    }
    
    private void RemoveFKLinkLine(FKLinkModel fkLink)
    {
        if (_canvas == null || !_fkLinkLines.ContainsKey(fkLink)) return;
        
        var line = _fkLinkLines[fkLink];
        _canvas.Children.Remove(line);
        _fkLinkLines.Remove(fkLink);
    }
    
    private void UpdateFKLines()
    {
        foreach (var kvp in _fkLinkLines)
        {
            UpdateFKLineLine(kvp.Key, kvp.Value);
        }
    }
    
    private void UpdateFKLineLine(FKLinkModel fkLink, Line line)
    {
        // Find table cards
        var sourceCard = FindTableCard(fkLink.SourceTable);
        var targetCard = FindTableCard(fkLink.TargetTable);
        
        if (sourceCard != null && targetCard != null)
        {
            // Calculate center points
            var sourceX = fkLink.SourceTable.X + sourceCard.Bounds.Width / 2;
            var sourceY = fkLink.SourceTable.Y + sourceCard.Bounds.Height / 2;
            var targetX = fkLink.TargetTable.X + targetCard.Bounds.Width / 2;
            var targetY = fkLink.TargetTable.Y + targetCard.Bounds.Height / 2;
            
            line.StartPoint = new Point(sourceX, sourceY);
            line.EndPoint = new Point(targetX, targetY);
        }
    }
    
    private TableCard? FindTableCard(TableModel table)
    {
        if (_canvas == null) return null;
        
        return _canvas.Children
            .OfType<TableCard>()
            .FirstOrDefault(card => card.DataContext == table);
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
    
    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveSchemaAsync();
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
                
                // Create FK link model
                var fkLink = new FKLinkModel
                {
                    SourceTable = _fkSourceTable,
                    SourceAttribute = _fkSourceAttribute,
                    TargetTable = clickedTable,
                    TargetAttribute = refAttribute
                };
                
                _viewModel.FKLinks.Add(fkLink);
            }
        }
        
        // Clean up temporary line
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
