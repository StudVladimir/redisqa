using StackExchange.Redis;
using redisqa.Services;

namespace redisqa.RedisConnect;

public class RedisConnect
{
    public IDatabase? GetDatabase(int dbIndex = 0)
    {
        return RedisConnectionService.Instance.GetDatabase(dbIndex);
    }
    
    public ConnectionMultiplexer? GetConnection()
    {
        return RedisConnectionService.Instance.GetConnection();
    }
    
    public bool IsConnected => RedisConnectionService.Instance.IsConnected;
}
