using System;
using System.Threading.Tasks;

namespace redisqa.Services;

public class GetSchemaFromRedis
{
    private static GetSchemaFromRedis? _instance;
    private static readonly object _lock = new object();
    
    public static GetSchemaFromRedis Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new GetSchemaFromRedis();
                }
                return _instance;
            }
        }
    }
    
    private string? _cachedSchemaJson;
    
    private GetSchemaFromRedis()
    {
        // Private constructor for singleton
    }
    
    /// <summary>
    /// Gets the database schema from Redis
    /// </summary>
    /// <param name="selectedDb">Number of selected Redis space (0-15)</param>
    /// <returns>JSON string with the schema or null if the schema is not found</returns>
    public async Task<string?> GetSchemaAsync(int selectedDb)
    {
        try
        {
            // Check Redis connection
            if (!RedisConnectionService.Instance.IsConnected)
            {
                throw new Exception("Not connected to Redis");
            }
            
            // Get the database with the selected index
            var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
            if (db == null)
            {
                throw new Exception($"Failed to get database {selectedDb}");
            }
            
            // Key to get the schema (the same used when saving)
            var redisKey = "ERD-SCHEMA";
            
            // Get the value from Redis
            var schemaValue = await db.StringGetAsync(redisKey);
            
            if (schemaValue.IsNullOrEmpty)
            {
                System.Diagnostics.Debug.WriteLine($"Schema not found in Redis with key: {redisKey} in database {selectedDb}");
                _cachedSchemaJson = null;
                return null;
            }
            
            // Save to cache and return
            _cachedSchemaJson = schemaValue.ToString();
            System.Diagnostics.Debug.WriteLine($"Schema loaded from Redis with key: {redisKey} in database {selectedDb}");
            
            return _cachedSchemaJson;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading schema from Redis: {ex.Message}");
            _cachedSchemaJson = null;
            throw;
        }
    }
    
    /// <summary>
    /// Gets the cached schema (the last loaded one)
    /// </summary>
    public string? GetCachedSchema()
    {
        return _cachedSchemaJson;
    }
    
    /// <summary>
    /// Clears the cached schema
    /// </summary>
    public void ClearCache()
    {
        _cachedSchemaJson = null;
    }
}
