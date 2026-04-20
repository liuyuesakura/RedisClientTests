using System.Text.Json;
using StackExchange.Redis;

public sealed class RedisRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<IDatabase> _databaseFactory;

    public RedisRepository(IDatabase database)
    {
        _databaseFactory = () => database;
    }

    public RedisRepository(RedisConnectionPool pool)
    {
        _databaseFactory = () => pool.GetDatabase();
    }

    public bool Set(string key, string value, TimeSpan? expiry = null) =>
        expiry.HasValue
            ? _databaseFactory().StringSet(key, value, expiry.Value)
            : _databaseFactory().StringSet(key, value);

    public string? GetString(string key)
    {
        var value = _databaseFactory().StringGet(key);
        return value.HasValue ? value.ToString() : null;
    }

    public bool Set(string key, byte[] value, TimeSpan? expiry = null) =>
        expiry.HasValue
            ? _databaseFactory().StringSet(key, value, expiry.Value)
            : _databaseFactory().StringSet(key, value);

    public byte[]? GetBytes(string key)
    {
        var value = _databaseFactory().StringGet(key);
        return value.HasValue ? (byte[]?)value : null;
    }

    public bool Set(string key, int value, TimeSpan? expiry = null) =>
        expiry.HasValue
            ? _databaseFactory().StringSet(key, value, expiry.Value)
            : _databaseFactory().StringSet(key, value);

    public int? GetInt(string key)
    {
        var value = _databaseFactory().StringGet(key);
        if (!value.HasValue)
        {
            return null;
        }

        return int.TryParse(value.ToString(), out var result) ? result : null;
    }

    public bool Set(string key, bool value, TimeSpan? expiry = null) =>
        expiry.HasValue
            ? _databaseFactory().StringSet(key, value ? "1" : "0", expiry.Value)
            : _databaseFactory().StringSet(key, value ? "1" : "0");

    public bool? GetBool(string key)
    {
        var value = _databaseFactory().StringGet(key);
        if (!value.HasValue)
        {
            return null;
        }

        var raw = value.ToString();
        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        return bool.TryParse(raw, out var parsed) ? parsed : null;
    }

    public bool Set<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return expiry.HasValue
            ? _databaseFactory().StringSet(key, json, expiry.Value)
            : _databaseFactory().StringSet(key, json);
    }

    public T? Get<T>(string key)
    {
        var value = _databaseFactory().StringGet(key);
        if (!value.HasValue)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!, JsonOptions);
    }

    public bool Expire(string key, TimeSpan expiry) =>
        _databaseFactory().KeyExpire(key, expiry);

    public bool HSet(string key, string field, string value) =>
        _databaseFactory().HashSet(key, field, value);

    public string? HGetString(string key, string field)
    {
        var value = _databaseFactory().HashGet(key, field);
        return value.HasValue ? value.ToString() : null;
    }

    public bool HSet(string key, string field, byte[] value) =>
        _databaseFactory().HashSet(key, field, value);

    public byte[]? HGetBytes(string key, string field)
    {
        var value = _databaseFactory().HashGet(key, field);
        return value.HasValue ? (byte[]?)value : null;
    }

    public bool HSet(string key, string field, int value) =>
        _databaseFactory().HashSet(key, field, value);

    public int? HGetInt(string key, string field)
    {
        var value = _databaseFactory().HashGet(key, field);
        if (!value.HasValue)
        {
            return null;
        }

        return int.TryParse(value.ToString(), out var result) ? result : null;
    }

    public bool HSet(string key, string field, bool value) =>
        _databaseFactory().HashSet(key, field, value ? "1" : "0");

    public bool? HGetBool(string key, string field)
    {
        var value = _databaseFactory().HashGet(key, field);
        if (!value.HasValue)
        {
            return null;
        }

        var raw = value.ToString();
        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        return bool.TryParse(raw, out var parsed) ? parsed : null;
    }

    public bool HSet<T>(string key, string field, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return _databaseFactory().HashSet(key, field, json);
    }

    public T? HGet<T>(string key, string field)
    {
        var value = _databaseFactory().HashGet(key, field);
        if (!value.HasValue)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!, JsonOptions);
    }

    public async Task PipelineSetAsync(
        IReadOnlyDictionary<string, RedisValue> values,
        TimeSpan? expiry = null)
    {
        if (values.Count == 0)
        {
            return;
        }

        var database = _databaseFactory();
        var batch = database.CreateBatch();
        var tasks = new List<Task>(values.Count);

        foreach (var item in values)
        {
            tasks.Add(
                expiry.HasValue
                    ? batch.StringSetAsync(item.Key, item.Value, expiry.Value)
                    : batch.StringSetAsync(item.Key, item.Value));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<Dictionary<string, string?>> PipelineGetStringAsync(IReadOnlyList<string> keys)
    {
        var result = new Dictionary<string, string?>(keys.Count);
        if (keys.Count == 0)
        {
            return result;
        }

        var database = _databaseFactory();
        var batch = database.CreateBatch();
        var tasks = new Dictionary<string, Task<RedisValue>>(keys.Count);

        foreach (var key in keys)
        {
            tasks[key] = batch.StringGetAsync(key);
        }

        batch.Execute();
        await Task.WhenAll(tasks.Values);

        foreach (var item in tasks)
        {
            var value = await item.Value;
            result[item.Key] = value.HasValue ? value.ToString() : null;
        }

        return result;
    }
}
