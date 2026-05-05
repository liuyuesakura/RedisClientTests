using StackExchange.Redis;

public sealed class CacheShell
{
    private static readonly TimeSpan DefaultRetryBaseDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly RedisRepository _repository;
    private readonly RedisConnectionPool _pool;
    private readonly Action<string>? _logger;

    public CacheShell(RedisRepository repository, RedisConnectionPool pool, Action<string>? logger = null)
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
        var cached = await _repository.GetAsync<T>(key);
        if (cached is not null)
        {
            return cached;
        }

        var lockKey = $"{key}:lock";
        var lockToken = Guid.NewGuid().ToString("N");
        var lockOk = await TryAcquireLockAsync(lockKey, lockToken, lockExpiry ?? TimeSpan.FromSeconds(5));

        if (lockOk)
        {
            try
            {
                var secondRead = await _repository.GetAsync<T>(key);
                if (secondRead is not null)
                {
                    return secondRead;
                }

                var data = await dataFactory();
                if (data is not null)
                {
                    await _repository.SetAsync(key, data, expiry);
                }

                return data;
            }
            finally
            {
                await ReleaseLockAsync(lockKey, lockToken);
            }
        }

        var baseDelay = retryDelay ?? DefaultRetryBaseDelay;
        for (var i = 0; i < retryCount; i++)
        {
            await Task.Delay(ComputeRetryDelay(baseDelay, i));
            var retried = await _repository.GetAsync<T>(key);
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
        var cached = await _repository.HGetAsync<T>(key, field);
        if (cached is not null)
        {
            return cached;
        }

        var lockKey = $"{key}:{field}:lock";
        var lockToken = Guid.NewGuid().ToString("N");
        var lockOk = await TryAcquireLockAsync(lockKey, lockToken, lockExpiry ?? TimeSpan.FromSeconds(5));

        if (lockOk)
        {
            try
            {
                var secondRead = await _repository.HGetAsync<T>(key, field);
                if (secondRead is not null)
                {
                    return secondRead;
                }

                var data = await dataFactory();
                if (data is not null)
                {
                    await _repository.HSetAsync(key, field, data);
                    await _repository.ExpireAsync(key, expiry);
                }

                return data;
            }
            finally
            {
                await ReleaseLockAsync(lockKey, lockToken);
            }
        }

        var baseDelay = retryDelay ?? DefaultRetryBaseDelay;
        for (var i = 0; i < retryCount; i++)
        {
            await Task.Delay(ComputeRetryDelay(baseDelay, i));
            var retried = await _repository.HGetAsync<T>(key, field);
            if (retried is not null)
            {
                return retried;
            }
        }

        _logger?.Invoke($"CacheShell fallback to direct data factory for hash: {key}/{field}");
        return await dataFactory();
    }

    private async Task<bool> TryAcquireLockAsync(string lockKey, string lockToken, TimeSpan lockExpiry)
    {
        var db = _pool.GetDatabase();
        return await db.StringSetAsync(lockKey, lockToken, lockExpiry, When.NotExists);
    }

    private async Task ReleaseLockAsync(string lockKey, string lockToken)
    {
        const string releaseScript = """
                                     if redis.call('GET', KEYS[1]) == ARGV[1] then
                                       return redis.call('DEL', KEYS[1])
                                     end
                                     return 0
                                     """;
        var db = _pool.GetDatabase();
        await db.ScriptEvaluateAsync(releaseScript, [lockKey], [lockToken]);
    }

    private static TimeSpan ComputeRetryDelay(TimeSpan baseDelay, int attempt)
    {
        var expFactor = 1 << Math.Min(attempt, 3);
        var delayMs = Math.Min(baseDelay.TotalMilliseconds * expFactor, MaxRetryDelay.TotalMilliseconds);
        var jitterMs = Random.Shared.NextDouble() * 15;
        return TimeSpan.FromMilliseconds(delayMs + jitterMs);
    }
}
