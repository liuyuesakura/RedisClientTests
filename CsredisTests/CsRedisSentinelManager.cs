// 哨兵连接与管理：整文件暂时不参与编译；需要时去掉 #if false。
#if false
using System.Threading;
using CSRedis;
using Polly;
using Polly.Retry;

public sealed class CsRedisSentinelManager : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _serviceName;
    private readonly CsRedisConnectionPool _pool;
    private readonly Action<string>? _logger;
    private readonly string _sentinelHost;
    private readonly int _sentinelPort;
    private readonly ResiliencePipeline _reconnectRetryPipeline;
    private readonly ResiliencePipeline _sentinelRecoveryRetryPipeline;
    private RedisSentinelClient _sentinel;
    private CancellationTokenSource? _healthCheckCts;
    private Task? _healthCheckTask;
    private bool _disposed;

    public CsRedisSentinelManager(
        string[] sentinelEndpoints,
        string serviceName,
        CsRedisConnectionPool pool,
        Action<string>? logger = null)
    {
        if (sentinelEndpoints.Length == 0)
        {
            throw new ArgumentException("At least one sentinel endpoint is required.", nameof(sentinelEndpoints));
        }

        var (host, port) = ParseEndpoint(sentinelEndpoints[0]);
        _sentinelHost = host;
        _sentinelPort = port;
        _serviceName = serviceName;
        _pool = pool;
        _logger = logger;

        _sentinel = CreateSentinelClient();
        _reconnectRetryPipeline = BuildReconnectRetryPipeline();
        _sentinelRecoveryRetryPipeline = BuildSentinelRecoveryRetryPipeline();
    }

    public event Action<string>? MasterDownDetected;
    public event Action<string>? MasterSwitched;

    public async Task StartAsync(bool enableHealthCheck = true, TimeSpan? healthCheckInterval = null)
    {
        ThrowIfDisposed();
        await Task.Run(() =>
        {
            lock (_syncRoot)
            {
                if (!_sentinel.Connect(5000))
                {
                    throw new InvalidOperationException("Failed to connect to Redis Sentinel.");
                }

                _sentinel.SubscriptionReceived += OnSubscriptionReceived;
                _sentinel.Subscribe(new[] { "+sdown", "+odown", "+switch-master" });
            }
        });

        _logger?.Invoke("Sentinel subscriptions are active.");

        if (enableHealthCheck)
        {
            StartHealthCheck(healthCheckInterval ?? TimeSpan.FromSeconds(10));
        }
    }

    private void OnSubscriptionReceived(object? sender, RedisSubscriptionReceivedEventArgs e)
    {
        var payload = e.Message?.ToString() ?? string.Empty;
        if (!IsRelatedToService(payload))
        {
            return;
        }

        if (payload.Contains("sdown", StringComparison.OrdinalIgnoreCase) ||
            payload.Contains("odown", StringComparison.OrdinalIgnoreCase))
        {
            MasterDownDetected?.Invoke(payload);
            HandleMasterDown(payload);
            return;
        }

        if (payload.Contains("switch-master", StringComparison.OrdinalIgnoreCase) ||
            payload.Contains("switch_master", StringComparison.OrdinalIgnoreCase))
        {
            MasterSwitched?.Invoke(payload);
            HandleMasterSwitched(payload);
        }
    }

    public void HandleMasterDown(string message)
    {
        _logger?.Invoke($"Master down detected: {message}");
        ExecuteReconnectWithRetry("master-down");
    }

    public void HandleMasterSwitched(string message)
    {
        _logger?.Invoke($"Master switched: {message}");
        ExecuteReconnectWithRetry("master-switched");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopHealthCheck();
        lock (_syncRoot)
        {
            _sentinel.SubscriptionReceived -= OnSubscriptionReceived;
            _sentinel.Dispose();
        }
    }

    private bool IsRelatedToService(string message) =>
        message.Contains($" {_serviceName} ", StringComparison.OrdinalIgnoreCase) ||
        message.StartsWith(_serviceName, StringComparison.OrdinalIgnoreCase) ||
        message.Contains($"master {_serviceName}", StringComparison.OrdinalIgnoreCase);

    private ResiliencePipeline BuildReconnectRetryPipeline()
    {
        var retryOptions = new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            OnRetry = args =>
            {
                _logger?.Invoke(
                    $"Reconnect retry {args.AttemptNumber + 1} failed: {args.Outcome.Exception?.Message}");
                return ValueTask.CompletedTask;
            }
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();
    }

    private ResiliencePipeline BuildSentinelRecoveryRetryPipeline()
    {
        var retryOptions = new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            OnRetry = args =>
            {
                _logger?.Invoke(
                    $"Sentinel recovery retry {args.AttemptNumber + 1} failed: {args.Outcome.Exception?.Message}");
                return ValueTask.CompletedTask;
            }
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();
    }

    private void ExecuteReconnectWithRetry(string reason)
    {
        try
        {
            _reconnectRetryPipeline.Execute(() => _pool.ForceReconnect());
            _logger?.Invoke($"Reconnect succeeded after {reason} event.");
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Reconnect failed after {reason} event: {ex.Message}");
        }
    }

    private void StartHealthCheck(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Health check interval must be greater than zero.");
        }

        lock (_syncRoot)
        {
            if (_healthCheckTask is not null)
            {
                return;
            }

            _healthCheckCts = new CancellationTokenSource();
            _healthCheckTask = RunHealthCheckLoopAsync(interval, _healthCheckCts.Token);
        }
    }

    private void StopHealthCheck()
    {
        lock (_syncRoot)
        {
            if (_healthCheckCts is null)
            {
                return;
            }

            _healthCheckCts.Cancel();
            _healthCheckCts.Dispose();
            _healthCheckCts = null;
            _healthCheckTask = null;
        }
    }

    private async Task RunHealthCheckLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_syncRoot)
                    {
                        _sentinel.Ping();
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Sentinel health check failed: {ex.Message}");
                RecoverSentinelConnectionWithRetry();
            }
        }
    }

    private void RecoverSentinelConnectionWithRetry()
    {
        try
        {
            _sentinelRecoveryRetryPipeline.Execute(() =>
            {
                lock (_syncRoot)
                {
                    _sentinel.SubscriptionReceived -= OnSubscriptionReceived;
                    _sentinel.Dispose();
                    _sentinel = CreateSentinelClient();
                    if (!_sentinel.Connect(5000))
                    {
                        throw new InvalidOperationException("Sentinel reconnect failed.");
                    }

                    _sentinel.SubscriptionReceived += OnSubscriptionReceived;
                    _sentinel.Subscribe(new[] { "+sdown", "+odown", "+switch-master" });
                }
            });
            _logger?.Invoke("Sentinel connection recovered and subscriptions restored.");
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Sentinel recovery failed: {ex.Message}");
        }
    }

    private RedisSentinelClient CreateSentinelClient() =>
        new(_sentinelHost, _sentinelPort);

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid sentinel endpoint: {endpoint}", nameof(endpoint));
        }

        return (parts[0], int.Parse(parts[1]));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CsRedisSentinelManager));
        }
    }
}
#endif
