using System.Threading;
using StackExchange.Redis;

public sealed class RedisConnectionPool : IDisposable
{
    private readonly object _syncRoot = new();
    private int _interactiveReconnectRunning;
    private ConfigurationOptions _options;
    private ConnectionMultiplexer[] _connectionArray;
    private readonly int _poolSize;
    private int _index = -1;
    private bool _disposed;

    public RedisConnectionPool(string connectionString, int poolSize = 4)
        : this(ConfigurationOptions.Parse(connectionString), poolSize)
    {
    }

    public RedisConnectionPool(ConfigurationOptions options, int poolSize = 4)
    {
        if (poolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(poolSize), "PoolSize must be greater than zero.");
        }

        _poolSize = poolSize;
        _options = CloneOptions(options);
        _connectionArray = CreateConnectionMultiplexers(_options, _poolSize, shortReconnectWindow: false);
    }

    public IDatabase GetDatabase(int db = -1)
    {
        ThrowIfDisposed();
        var next = Interlocked.Increment(ref _index);
        var connection = _connectionArray[next % _connectionArray.Length];
        return connection.GetDatabase(db);
    }

    public void ForceReconnect()
    {
        ThrowIfDisposed();
        lock (_syncRoot)
        {
            var newConnections = CreateConnectionMultiplexers(_options, _poolSize, shortReconnectWindow: true);
            SwapConnections(newConnections);
        }
    }

    public void Reconfigure(ConfigurationOptions options)
    {
        ThrowIfDisposed();
        lock (_syncRoot)
        {
            _options = CloneOptions(options);
            var newConnections = CreateConnectionMultiplexers(_options, _poolSize, shortReconnectWindow: false);
            SwapConnections(newConnections);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var connection in _connectionArray)
        {
            connection.Dispose();
        }
    }

    private void SwapConnections(ConnectionMultiplexer[] newConnections)
    {
        var oldConnections = _connectionArray;
        _connectionArray = newConnections;
        Interlocked.Exchange(ref _index, -1);

        foreach (var oldConnection in oldConnections)
        {
            oldConnection.Dispose();
        }
    }

    private ConnectionMultiplexer[] CreateConnectionMultiplexers(ConfigurationOptions options, int poolSize, bool shortReconnectWindow)
    {
        var array = new ConnectionMultiplexer[poolSize];
        for (var i = 0; i < poolSize; i++)
        {
            var mux = ConnectMultiplexerWithFailoverRetry(options, shortReconnectWindow);
            array[i] = mux;
            mux.ConnectionFailed += (_, a) =>
            {
                if (a.ConnectionType == ConnectionType.Interactive &&
                    (a.FailureType == ConnectionFailureType.UnableToConnect ||
                     a.FailureType == ConnectionFailureType.SocketClosed))
                {
                    ScheduleDebouncedInteractiveReconnect(a.FailureType, a.EndPoint?.ToString());
                }
            };
        }

        return array;
    }

    /// <summary>故障转移时 Connect 可能短暂失败；首次建连用略多重试，ForceReconnect 用短重试避免与 ConnectionFailed 风暴叠加成“无限重试”。</summary>
    private static ConnectionMultiplexer ConnectMultiplexerWithFailoverRetry(ConfigurationOptions options, bool shortReconnectWindow)
    {
        var maxAttempts = shortReconnectWindow ? 3 : 5;
        var maxBackoffMs = shortReconnectWindow ? 1200 : 3000;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return ConnectionMultiplexer.Connect(CloneOptions(options));
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt >= maxAttempts)
                {
                    break;
                }

                Thread.Sleep(Math.Min(100 * attempt, maxBackoffMs));
            }
        }

        throw new InvalidOperationException(
            $"Could not connect to Redis after {maxAttempts} attempts (failover / Sentinel window).",
            last);
    }

    private void ScheduleDebouncedInteractiveReconnect(ConnectionFailureType failureType, string? endPoint)
    {
        if (Interlocked.CompareExchange(ref _interactiveReconnectRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                ForceReconnect();
            }
            catch
            {
                // Interactive reconnect best-effort; next failure can retry.
            }
            finally
            {
                Interlocked.Exchange(ref _interactiveReconnectRunning, 0);
            }
        });
    }

    private static ConfigurationOptions CloneOptions(ConfigurationOptions options) =>
        ConfigurationOptions.Parse(options.ToString());

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisConnectionPool));
        }
    }
}
