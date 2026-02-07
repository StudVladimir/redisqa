using System;
using System.Threading.Tasks;
using redisqa.Services;

namespace redisqa.ViewModels;

public class ConnectionWindowViewModel : BaseViewModel
{
    private string _instanceName = Environment.GetEnvironmentVariable("REDIS_INSTANCE_NAME") ?? "Local Redis";
    private string _host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
    private int _port = int.TryParse(Environment.GetEnvironmentVariable("REDIS_PORT"), out var p) ? p : 6379;
    private string _password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? "";
    private string _statusMessage = "";
    private bool _isConnecting = false;
    private bool _hasError = false;
    
    public string InstanceName
    {
        get => _instanceName;
        set
        {
            _instanceName = value;
            OnPropertyChanged();
        }
    }
    
    public string Host
    {
        get => _host;
        set
        {
            _host = value;
            OnPropertyChanged();
        }
    }
    
    public int Port
    {
        get => _port;
        set
        {
            _port = value;
            OnPropertyChanged();
        }
    }
    
    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged();
        }
    }
    
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }
    
    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            OnPropertyChanged();
        }
    }
    
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _isConnecting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConnect));
        }
    }
    
    public bool CanConnect => !IsConnecting;
    
    public async Task<bool> ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            StatusMessage = "Instance name is required";
            HasError = true;
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(Host))
        {
            StatusMessage = "Host is required";
            HasError = true;
            return false;
        }
        
        if (Port <= 0 || Port > 65535)
        {
            StatusMessage = "Invalid port number";
            HasError = true;
            return false;
        }
        
        IsConnecting = true;
        HasError = false;
        StatusMessage = "Connecting...";
        
        try
        {
            var service = RedisConnectionService.Instance;
            var password = string.IsNullOrWhiteSpace(Password) ? null : Password;
            
            StatusMessage = $"Connecting to {Host}:{Port}...";
            var success = await service.ConnectAsync(InstanceName, Host, Port, password);
            
            if (success)
            {
                StatusMessage = $"✓ Successfully connected to {Host}:{Port}";
                HasError = false;
                await Task.Delay(500); // Показать сообщение об успехе
                return true;
            }
            else
            {
                StatusMessage = "Connection failed. Please check your settings.";
                HasError = true;
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection Error: {ex.Message}";
            HasError = true;
            return false;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
