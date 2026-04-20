using FreeRedis;

/// <summary>维护多个 <see cref="RedisClient"/>（集群多节点由 FreeRedis 内部处理），实现与 CSRedis/SE 测试项目类似的池化。</summary>
public sealed class FreeRedisConnectionPool : IDisposable
{
    private readonly object _syncRoot = new();
    private RedisClient[] _clients = [];
    private int _index = -1;
    private readonly Func<RedisClient> _clientFactory;
    private bool _disposed;

    public FreeRedisConnectionPool(string connectionString, int poolSize = 4)
        : this(() => CreateClusterClient(connectionString), poolSize)
    {
    }

    private FreeRedisConnectionPool(Func<RedisClient> clientFactory, int poolSize)
    {
        if (poolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(poolSize), "PoolSize must be greater than zero.");
        }

        _clientFactory = clientFactory;
        _clients = new RedisClient[poolSize];
        for (var i = 0; i < poolSize; i++)
        {
            _clients[i] = _clientFactory();
        }
    }

    public RedisClient Client
    {
        get
        {
            ThrowIfDisposed();
            var next = Interlocked.Increment(ref _index);
            return _clients[next % _clients.Length];
        }
    }

    public void ForceReconnect()
    {
        ThrowIfDisposed();
        lock (_syncRoot)
        {
            var poolSize = _clients.Length;
            var newClients = new RedisClient[poolSize];
            for (var i = 0; i < poolSize; i++)
            {
                newClients[i] = _clientFactory();
            }

            var old = _clients;
            _clients = newClients;
            Interlocked.Exchange(ref _index, -1);

            foreach (var c in old)
            {
                c.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var c in _clients)
        {
            c.Dispose();
        }
    }

    private static RedisClient CreateClusterClient(string connectionString) =>
        FreeRedisClusterFactory.Create(connectionString);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FreeRedisConnectionPool));
        }
    }
}
