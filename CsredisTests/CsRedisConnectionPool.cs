using CSRedis;

public sealed class CsRedisConnectionPool : IDisposable
{
    private readonly object _syncRoot = new();
    private CSRedisClient _client;
    private readonly Func<CSRedisClient> _clientFactory;
    private bool _disposed;

    public CsRedisConnectionPool(string connectionString, int poolSize = 4)
        : this(() => new CSRedisClient(AppendPoolSize(connectionString, poolSize)))
    {
    }

    // 哨兵连接：已注释（与 Program 中 Sentinel 分支一致关闭）
    // public CsRedisConnectionPool(string sentinelConnectionString, string[] sentinelEndpoints, int poolSize, bool readOnly = false)
    //     : this(() => new CSRedisClient(
    //         AppendPoolSize(sentinelConnectionString, poolSize),
    //         sentinelEndpoints,
    //         readOnly))
    // {
    // }

    private CsRedisConnectionPool(Func<CSRedisClient> clientFactory)
    {
        _clientFactory = clientFactory;
        _client = _clientFactory();
    }

    public CSRedisClient Client
    {
        get
        {
            ThrowIfDisposed();
            return _client;
        }
    }

    public void ForceReconnect()
    {
        ThrowIfDisposed();
        lock (_syncRoot)
        {
            _client.Dispose();
            _client = _clientFactory();
            RedisHelper.Initialization(_client);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private static string AppendPoolSize(string connectionString, int poolSize)
    {
        if (connectionString.Contains("poolsize=", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var sep = connectionString.EndsWith(',') ? "" : ",";
        return $"{connectionString}{sep}poolsize={poolSize}";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CsRedisConnectionPool));
        }
    }
}
