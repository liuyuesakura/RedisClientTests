using Polly;
using Polly.Retry;
using StackExchange.Redis;

public sealed class RedisSentinelManager : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _serviceName;
    private readonly RedisConnectionPool _pool;
    private readonly Action<string>? _logger;
    private readonly ConfigurationOptions _sentinelOptions;
    private readonly ResiliencePipeline _reconnectRetryPipeline;
    private readonly ResiliencePipeline _sentinelRecoveryRetryPipeline;
    private ConnectionMultiplexer _sentinelConnection;
    private ISubscriber _subscriber;
    private CancellationTokenSource? _healthCheckCts;
    private Task? _healthCheckTask;
    private bool _disposed;

    public RedisSentinelManager(
        ConfigurationOptions sentinelOptions,
        string serviceName,
        RedisConnectionPool pool,
        Action<string>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("ServiceName is required.", nameof(serviceName));
        }

        _serviceName = serviceName;
        _pool = pool;
        _logger = logger;

        sentinelOptions.CommandMap = CommandMap.Sentinel;
        sentinelOptions.AbortOnConnectFail = false;
        sentinelOptions.TieBreaker = string.Empty;

        _sentinelOptions = ConfigurationOptions.Parse(sentinelOptions.ToString());
        _sentinelConnection = ConnectionMultiplexer.Connect(_sentinelOptions);
        _subscriber = _sentinelConnection.GetSubscriber();
        _reconnectRetryPipeline = BuildReconnectRetryPipeline();
        _sentinelRecoveryRetryPipeline = BuildSentinelRecoveryRetryPipeline();
    }

    public event Action<string>? MasterDownDetected;
    public event Action<string>? MasterSwitched;

    public async Task StartAsync(bool enableHealthCheck = true, TimeSpan? healthCheckInterval = null)
    {
        ThrowIfDisposed();
        await SubscribeSentinelChannelsAsync();

        _logger?.Invoke("Sentinel subscriptions are active.");

        if (enableHealthCheck)
        {
            StartHealthCheck(healthCheckInterval ?? TimeSpan.FromSeconds(10));
        }
    }

    private void OnSentinelMessage(RedisChannel channel, RedisValue message)
    {
        var payload = message.ToString();
        var related = IsRelatedToService(payload);
        if (!related)
        {
            return;
        }

        if (channel == "+sdown" || channel == "+odown")
        {
            MasterDownDetected?.Invoke(payload);
            HandleMasterDown(payload);
            return;
        }

        if (channel == "+switch-master")
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
        _sentinelConnection.Dispose();
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

    private async Task SubscribeSentinelChannelsAsync()
    {
        await _subscriber.SubscribeAsync(RedisChannel.Literal("+sdown"), OnSentinelMessage);
        await _subscriber.SubscribeAsync(RedisChannel.Literal("+odown"), OnSentinelMessage);
        await _subscriber.SubscribeAsync(RedisChannel.Literal("+switch-master"), OnSentinelMessage);
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
                await _sentinelConnection.GetDatabase().PingAsync();
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
                    _sentinelConnection.Dispose();
                    _sentinelConnection = ConnectionMultiplexer.Connect(
                        ConfigurationOptions.Parse(_sentinelOptions.ToString()));
                    _subscriber = _sentinelConnection.GetSubscriber();
                    SubscribeSentinelChannelsAsync().GetAwaiter().GetResult();
                }
            });
            _logger?.Invoke("Sentinel connection recovered and subscriptions restored.");
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Sentinel recovery failed: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisSentinelManager));
        }
    }
}
