using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using redisqa.Models;

namespace redisqa.Views;

public partial class TableCard : UserControl
{
    private bool _isDragging = false;
    private Point _dragStartPoint;
    
    // Event for FK link creation
    public event EventHandler<FKLinkEventArgs>? FKLinkRequested;
    
    // Event for table deletion
    public event EventHandler<TableModel>? TableDeleteRequested;
    
    // Event for attribute deletion
    public event EventHandler<AttributeDeleteEventArgs>? AttributeDeleteRequested;

    public TableCard()
    {
        InitializeComponent();
        
        // Set position when DataContext changes
        DataContextChanged += OnDataContextChanged;
        
        // Enable dragging
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        
        // Add attribute button handler
        var btnAddAttribute = this.FindControl<Button>("BtnAddAttribute");
        if (btnAddAttribute != null)
        {
            btnAddAttribute.Click += BtnAddAttribute_Click;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TableModel table)
        {
            // Subscribe to property changes
            table.PropertyChanged += Table_PropertyChanged;
            
            // Set initial position
            UpdatePosition(table);
        }
    }

    private void Table_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TableModel table && (e.PropertyName == nameof(TableModel.X) || e.PropertyName == nameof(TableModel.Y)))
        {
            UpdatePosition(table);
        }
    }

    private void UpdatePosition(TableModel table)
    {
        Canvas.SetLeft(this, table.X);
        Canvas.SetTop(this, table.Y);
    }

    private void BtnAddAttribute_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TableModel table)
        {
            table.Attributes.Add(new AttributeModel 
            { 
                Name = $"attribute_{table.Attributes.Count + 1}" 
            });
        }
    }
    
    private void BtnCreateFKLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AttributeModel attribute && DataContext is TableModel table)
        {
            // Raise event to start FK link creation
            FKLinkRequested?.Invoke(this, new FKLinkEventArgs(table, attribute));
        }
    }
    
    private void BtnDeleteAttribute_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AttributeModel attribute && DataContext is TableModel table)
        {
            // Raise event to notify SchemaView about attribute deletion
            AttributeDeleteRequested?.Invoke(this, new AttributeDeleteEventArgs(table, attribute));
            
            // Remove attribute from table
            table.Attributes.Remove(attribute);
        }
    }
    
    private void BtnDeleteTable_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TableModel table)
        {
            // Raise event to request table deletion
            TableDeleteRequested?.Invoke(this, table);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this.Parent as Visual);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && DataContext is TableModel table && this.Parent is Canvas canvas)
        {
            var currentPoint = e.GetPosition(canvas);
            var delta = currentPoint - _dragStartPoint;
            
            table.X += delta.X;
            table.Y += delta.Y;
            
            _dragStartPoint = currentPoint;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }
}

// Event args for FK link creation
public class FKLinkEventArgs : EventArgs
{
    public TableModel SourceTable { get; }
    public AttributeModel SourceAttribute { get; }
    
    public FKLinkEventArgs(TableModel sourceTable, AttributeModel sourceAttribute)
    {
        SourceTable = sourceTable;
        SourceAttribute = sourceAttribute;
    }
}

// Event args for attribute deletion
public class AttributeDeleteEventArgs : EventArgs
{
    public TableModel Table { get; }
    public AttributeModel Attribute { get; }
    
    public AttributeDeleteEventArgs(TableModel table, AttributeModel attribute)
    {
        Table = table;
        Attribute = attribute;
    }
}
