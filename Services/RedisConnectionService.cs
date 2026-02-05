using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace redisqa.Services;

public class RedisConnectionService
{
    private static RedisConnectionService? _instance;
    private static readonly object _lock = new object();
    
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;
    
    public string? InstanceName { get; private set; }
    public bool IsConnected => _redis?.IsConnected ?? false;
    
    public static RedisConnectionService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new RedisConnectionService();
                    }
                }
            }
            return _instance;
        }
    }
    
    private RedisConnectionService()
    {
    }
    
    public async Task<bool> ConnectAsync(string instanceName, string host, int port, string? password = null)
    {
        try
        {
            // Закрыть существующее подключение, если оно есть
            if (_redis != null)
            {
                await _redis.CloseAsync();
                _redis.Dispose();
            }
            
            // Создать строку подключения
            var configOptions = new ConfigurationOptions
            {
                EndPoints = { { host, port } },
                AbortOnConnectFail = false,
                ConnectTimeout = 5000,
                SyncTimeout = 5000
            };
            
            if (!string.IsNullOrWhiteSpace(password))
            {
                configOptions.Password = password;
            }
            
            // Подключиться к Redis
            _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
            _db = _redis.GetDatabase();
            
            // Проверить подключение командой PING
            try
            {
                var pingResult = await _db.PingAsync();
                if (pingResult.TotalMilliseconds < 0)
                {
                    throw new Exception("PING command failed");
                }
            }
            catch (Exception ex)
            {
                // Если PING не прошел, закрыть подключение
                if (_redis != null)
                {
                    await _redis.CloseAsync();
                    _redis.Dispose();
                }
                _redis = null;
                _db = null;
                InstanceName = null;
                throw new Exception($"Redis connection check failed: {ex.Message}");
            }
            
            InstanceName = instanceName;
            
            return true;
        }
        catch
        {
            _redis = null;
            _db = null;
            InstanceName = null;
            return false;
        }
    }
    
    public IDatabase? GetDatabase(int dbIndex = 0)
    {
        return _redis?.GetDatabase(dbIndex);
    }
    
    public ConnectionMultiplexer? GetConnection()
    {
        return _redis;
    }
    
    public async Task DisconnectAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
            _redis = null;
            _db = null;
            InstanceName = null;
        }
    }
}
