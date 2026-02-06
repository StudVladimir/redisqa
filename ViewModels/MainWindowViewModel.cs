using redisqa.Services;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace redisqa.ViewModels;

public class MainWindowViewModel: BaseViewModel
{
    private object? _currentView;
    private int? _selectedDb;
    private string _connectionStatus = "Not Connected";
    private bool _isConnected = false;
    private string _instanceName = "";
    private string _hostAddress = "";
    
    public object? CurrentView 
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
        }
    }

    public int? SelectedDb
    {
        get => _selectedDb;
        set
        {
            _selectedDb = value;
            OnPropertyChanged();
        }
    }
    
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            _connectionStatus = value;
            OnPropertyChanged();
        }
    }
    
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
        }
    }
    
    public string InstanceName
    {
        get => _instanceName;
        set
        {
            _instanceName = value;
            OnPropertyChanged();
        }
    }
    
    public string HostAddress
    {
        get => _hostAddress;
        set
        {
            _hostAddress = value;
            OnPropertyChanged();
        }
    }

    public MainWindowViewModel()
    {
        // Загружаем информацию о подключении
        LoadConnectionInfo();
        
        // При запуске показываем HomeView
        CurrentView = new Views.HomeView();
    }
    
    private void LoadConnectionInfo()
    {
        try
        {
            var service = RedisConnectionService.Instance;
            IsConnected = service.IsConnected;
            InstanceName = service.InstanceName ?? "Unknown";
            
            var connection = service.GetConnection();
            if (connection != null && connection.IsConnected)
            {
                var endpoints = connection.GetEndPoints();
                if (endpoints != null && endpoints.Length > 0)
                {
                    HostAddress = endpoints[0]?.ToString() ?? "Unknown";
                }
                else
                {
                    HostAddress = "Unknown";
                }
                ConnectionStatus = "Connected";
            }
            else
            {
                ConnectionStatus = "Not Connected";
                HostAddress = "N/A";
            }
        }
        catch (Exception)
        {
            IsConnected = false;
            ConnectionStatus = "Not Connected";
            InstanceName = "Error";
            HostAddress = "N/A";
        }
    }
    
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var service = RedisConnectionService.Instance;
            var db = service.GetDatabase();
            
            if (db != null)
            {
                var pingResult = await db.PingAsync();
                IsConnected = pingResult.TotalMilliseconds >= 0;
                ConnectionStatus = IsConnected ? "Connected" : "Not Connected";
                return IsConnected;
            }
            
            IsConnected = false;
            ConnectionStatus = "Not Connected";
            return false;
        }
        catch
        {
            IsConnected = false;
            ConnectionStatus = "Not Connected";
            return false;
        }
    }

    public void NavigateToHome()
    {
        CurrentView = new Views.HomeView();
    }

    public void NavigateToSchema()
    {
        var schemaView = new Views.SchemaView.SchemaView();
        // Передаем выбранную БД в SchemaViewModel
        if (schemaView.DataContext is SchemaViewModel schemaViewModel && SelectedDb.HasValue)
        {
            schemaViewModel.SelectedDb = SelectedDb.Value;
        }
        CurrentView = schemaView;
    }
}