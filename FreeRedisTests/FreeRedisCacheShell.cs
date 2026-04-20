using FreeRedis;

public sealed class FreeRedisCacheShell
{
    private readonly FreeRedisRepository _repository;
    private readonly FreeRedisConnectionPool _pool;
    private readonly Action<string>? _logger;

    public FreeRedisCacheShell(FreeRedisRepository repository, FreeRedisConnectionPool pool, Action<string>? logger = null)
    {
        _repository = repository;
        _pool = pool;
        _logger = logger;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        TimeSpan expiry,
        Func<Task<T?>> dataFactory,
        TimeSpan? lockExpiry = null,
        int retryCount = 10,
        TimeSpan? retryDelay = null)
    {
        var cached = _repository.Get<T>(key);
        if (cached is not null)
        {
            return cached;
        }

        var lockKey = $"{key}:lock";
        var lockToken = Guid.NewGuid().ToString("N");
        var lockOk = TryAcquireLock(lockKey, lockToken, lockExpiry ?? TimeSpan.FromSeconds(5));

        if (lockOk)
        {
            try
            {
                var secondRead = _repository.Get<T>(key);
                if (secondRead is not null)
                {
                    return secondRead;
                }

                var data = await dataFactory();
                if (data is not null)
                {
                    _repository.Set(key, data, expiry);
                }

                return data;
            }
            finally
            {
                ReleaseLock(lockKey, lockToken);
            }
        }

        var delay = retryDelay ?? TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < retryCount; i++)
        {
            await Task.Delay(delay);
            var retried = _repository.Get<T>(key);
            if (retried is not null)
            {
                return retried;
            }
        }

        _logger?.Invoke($"CacheShell fallback to direct data factory for key: {key}");
        return await dataFactory();
    }

    public async Task<T?> GetOrSetHashAsync<T>(
        string key,
        string field,
        TimeSpan expiry,
        Func<Task<T?>> dataFactory,
        TimeSpan? lockExpiry = null,
        int retryCount = 10,
        TimeSpan? retryDelay = null)
    {
        var cached = _repository.HGet<T>(key, field);
        if (cached is not null)
        {
            return cached;
        }

        var lockKey = $"{key}:{field}:lock";
        var lockToken = Guid.NewGuid().ToString("N");
        var lockOk = TryAcquireLock(lockKey, lockToken, lockExpiry ?? TimeSpan.FromSeconds(5));

        if (lockOk)
        {
            try
            {
                var secondRead = _repository.HGet<T>(key, field);
                if (secondRead is not null)
                {
                    return secondRead;
                }

                var data = await dataFactory();
                if (data is not null)
                {
                    _repository.HSet(key, field, data);
                    _repository.Expire(key, expiry);
                }

                return data;
            }
            finally
            {
                ReleaseLock(lockKey, lockToken);
            }
        }

        var delay = retryDelay ?? TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < retryCount; i++)
        {
            await Task.Delay(delay);
            var retried = _repository.HGet<T>(key, field);
            if (retried is not null)
            {
                return retried;
            }
        }

        _logger?.Invoke($"CacheShell fallback to direct data factory for hash: {key}/{field}");
        return await dataFactory();
    }

    private bool TryAcquireLock(string lockKey, string lockToken, TimeSpan lockExpiry)
    {
        var cli = _pool.Client;
        return cli.SetNx(lockKey, lockToken, lockExpiry);
    }

    private void ReleaseLock(string lockKey, string lockToken)
    {
        const string releaseScript = """
                                     if redis.call('GET', KEYS[1]) == ARGV[1] then
                                       return redis.call('DEL', KEYS[1])
                                     end
                                     return 0
                                     """;
        var cli = _pool.Client;
        _ = cli.Eval(releaseScript, new[] { lockKey }, new object[] { lockToken });
    }
}
