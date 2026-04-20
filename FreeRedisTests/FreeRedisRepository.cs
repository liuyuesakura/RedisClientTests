using System.Text.Json;
using FreeRedis;

public sealed class FreeRedisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<RedisClient> _clientFactory;

    public FreeRedisRepository(RedisClient client)
    {
        _clientFactory = () => client;
    }

    public FreeRedisRepository(FreeRedisConnectionPool pool)
    {
        _clientFactory = () => pool.Client;
    }

    public void Set(string key, string value, TimeSpan? expiry = null)
    {
        var c = _clientFactory();
        if (expiry.HasValue)
        {
            c.Set(key, value, expiry.Value);
        }
        else
        {
            c.Set(key, value);
        }
    }

    public string? GetString(string key) => _clientFactory().Get(key);

    public void Set<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var c = _clientFactory();
        if (expiry.HasValue)
        {
            c.Set(key, json, expiry.Value);
        }
        else
        {
            c.Set(key, json);
        }
    }

    public T? Get<T>(string key)
    {
        var value = _clientFactory().Get(key);
        if (string.IsNullOrEmpty(value))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value, JsonOptions);
    }

    public bool Expire(string key, TimeSpan expiry) => _clientFactory().Expire(key, expiry);

    public long HSet(string key, string field, string value) => _clientFactory().HSet(key, field, value);

    public string? HGetString(string key, string field) => _clientFactory().HGet(key, field);

    public long HSet<T>(string key, string field, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return _clientFactory().HSet(key, field, json);
    }

    public T? HGet<T>(string key, string field)
    {
        var value = _clientFactory().HGet(key, field);
        if (string.IsNullOrEmpty(value))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value, JsonOptions);
    }

    public Task PipelineSetAsync(IReadOnlyDictionary<string, string> values, TimeSpan? expiry = null)
    {
        if (values.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            var redis = _clientFactory();
            using var pipe = redis.StartPipe();
            foreach (var pair in values)
            {
                if (expiry.HasValue)
                {
                    pipe.Set(pair.Key, pair.Value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    pipe.Set(pair.Key, pair.Value);
                }
            }

            pipe.EndPipe();
        });
    }

    public Task<Dictionary<string, string?>> PipelineGetStringAsync(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return Task.FromResult(new Dictionary<string, string?>());
        }

        return Task.Run(() =>
        {
            var redis = _clientFactory();
            using var pipe = redis.StartPipe();
            foreach (var key in keys)
            {
                pipe.Get(key);
            }

            var raw = pipe.EndPipe();
            var result = new Dictionary<string, string?>(keys.Count);
            if (raw is object[] parts)
            {
                for (var i = 0; i < keys.Count && i < parts.Length; i++)
                {
                    var v = parts[i];
                    result[keys[i]] = v is null or DBNull ? null : v.ToString();
                }
            }
            else
            {
                for (var i = 0; i < keys.Count; i++)
                {
                    result[keys[i]] = null;
                }
            }

            return result;
        });
    }
}
