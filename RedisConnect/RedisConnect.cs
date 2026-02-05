using StackExchange.Redis;

namespace redisqa.RedisConnect;

public class RedisConnect
{
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;

}