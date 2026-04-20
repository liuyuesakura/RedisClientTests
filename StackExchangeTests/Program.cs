using System.Text.Json;
using StackExchange.Redis;

const string configPath = "appsettings.json";
if (!File.Exists(configPath))
{
    Console.WriteLine("Configuration file appsettings.json was not found.");
    return;
}

using var document = JsonDocument.Parse(File.ReadAllText(configPath));
var redisSection = document.RootElement.GetProperty("Redis");
var connectionString = redisSection.GetProperty("ConnectionString").GetString();
var poolSize = redisSection.TryGetProperty("PoolSize", out var poolSizeElement)
    ? poolSizeElement.GetInt32()
    : 4;
var sentinelEnabled = redisSection.TryGetProperty("SentinelEnabled", out var sentinelEnabledElement) &&
                      sentinelEnabledElement.GetBoolean();
var sentinelServiceName = redisSection.TryGetProperty("SentinelServiceName", out var serviceNameElement)
    ? serviceNameElement.GetString()
    : null;
var sentinelEndpoints = redisSection.TryGetProperty("SentinelEndpoints", out var endpointsElement) &&
                        endpointsElement.ValueKind == JsonValueKind.Array
    ? endpointsElement.EnumerateArray()
        .Select(item => item.GetString())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Cast<string>()
        .ToArray()
    : [];
// 哨兵 pub/sub 与健康检查（RedisSentinelManager）测试已关闭；需要时取消注释并恢复 StartAsync。
// var sentinelHealthCheckEnabled = redisSection.TryGetProperty("SentinelHealthCheckEnabled", out var healthCheckEnabledElement)
//     ? healthCheckEnabledElement.GetBoolean()
//     : true;
// var sentinelHealthCheckIntervalSeconds = redisSection.TryGetProperty("SentinelHealthCheckIntervalSeconds", out var healthCheckIntervalElement)
//     ? healthCheckIntervalElement.GetInt32()
//     : 10;

if (!sentinelEnabled && string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Redis:ConnectionString is not configured.");
    return;
}

var options = sentinelEnabled
    ? CreateSentinelRedisOptions(sentinelServiceName, sentinelEndpoints)
    : ConfigurationOptions.Parse(connectionString!);

using var redisPool = new RedisConnectionPool(options, poolSize);
var repository = new RedisRepository(redisPool);
var cacheShell = new CacheShell(repository, redisPool, message => Console.WriteLine($"[CacheShell] {message}"));
// using var sentinelManager = sentinelEnabled
//     ? CreateSentinelManager(redisPool, sentinelServiceName, sentinelEndpoints)
//     : null;
//
// if (sentinelManager is not null)
// {
//     sentinelManager.MasterDownDetected += message => Console.WriteLine($"[Sentinel] Master down: {message}");
//     sentinelManager.MasterSwitched += message => Console.WriteLine($"[Sentinel] Master switched: {message}");
//     await sentinelManager.StartAsync(
//         sentinelHealthCheckEnabled,
//         TimeSpan.FromSeconds(sentinelHealthCheckIntervalSeconds));
// }

Console.WriteLine($"Redis connected. Endpoint count: {options.EndPoints.Count}.");
Console.WriteLine($"RedisRepository ready with pool size: {poolSize}.");
Console.WriteLine($"CacheShell ready: {cacheShell.GetType().Name}.");
Console.WriteLine("Tests will repeat every 15 seconds. Press Ctrl+C to stop.");
while (true)
{
    try
    {
        Console.WriteLine($"--- Test run @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
        var anyReachable = await RedisReachableNodesReport.PrintAsync(sentinelEnabled, connectionString, sentinelEndpoints);
        if (!anyReachable)
        {
            Console.WriteLine("[MainLoop] No reachable node in topology probe; skipping Pipeline/CacheShell. Retry in 3s.");
            await Task.Delay(TimeSpan.FromSeconds(3));
            continue;
        }

        await RunPipelineTestAsync(repository);
        await RunCacheShellTestAsync(cacheShell);
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MainLoop] Redis/test error (will retry): {ex.GetType().Name}: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}

static ConfigurationOptions CreateSentinelRedisOptions(string? serviceName, string[] endpoints)
{
    if (string.IsNullOrWhiteSpace(serviceName))
    {
        throw new InvalidOperationException("Redis:SentinelServiceName is required when Sentinel is enabled.");
    }

    if (endpoints.Length == 0)
    {
        throw new InvalidOperationException("Redis:SentinelEndpoints is required when Sentinel is enabled.");
    }

    var options = new ConfigurationOptions
    {
        ServiceName = serviceName,
        AbortOnConnectFail = false,
        TieBreaker = string.Empty
    };

    foreach (var endpoint in endpoints)
    {
        options.EndPoints.Add(endpoint);
    }

    return options;
}

// static RedisSentinelManager CreateSentinelManager(
//     RedisConnectionPool pool,
//     string? serviceName,
//     string[] endpoints)
// {
//     if (string.IsNullOrWhiteSpace(serviceName))
//     {
//         throw new InvalidOperationException("Redis:SentinelServiceName is required when Sentinel is enabled.");
//     }
//
//     if (endpoints.Length == 0)
//     {
//         throw new InvalidOperationException("Redis:SentinelEndpoints is required when Sentinel is enabled.");
//     }
//
//     var sentinelOptions = new ConfigurationOptions
//     {
//         AbortOnConnectFail = false,
//         TieBreaker = string.Empty
//     };
//
//     foreach (var endpoint in endpoints)
//     {
//         sentinelOptions.EndPoints.Add(endpoint);
//     }
//
//     return new RedisSentinelManager(
//         sentinelOptions,
//         serviceName,
//         pool,
//         message => Console.WriteLine($"[Sentinel] {message}"));
// }

static async Task RunPipelineTestAsync(RedisRepository repository)
{
    var prefix = $"pipeline:test:{Guid.NewGuid():N}";
    var values = new Dictionary<string, RedisValue>
    {
        [$"{prefix}:k1"] = "v1",
        [$"{prefix}:k2"] = "v2",
        [$"{prefix}:k3"] = "v3"
    };

    await repository.PipelineSetAsync(values, TimeSpan.FromMinutes(5));
    var readResult = await repository.PipelineGetStringAsync(values.Keys.ToList());

    Console.WriteLine("[PipelineTest] Start");
    foreach (var key in values.Keys)
    {
        Console.WriteLine($"[PipelineTest] {key} => {readResult[key]}");
    }
    Console.WriteLine("[PipelineTest] End");
}

static async Task RunCacheShellTestAsync(CacheShell cacheShell)
{
    var prefix = $"cacheshell:test:{Guid.NewGuid():N}";
    var expiry = TimeSpan.FromMinutes(5);

    var stringFactoryCalls = 0;
    var stringKey = $"{prefix}:string";

    var stringFirst = await cacheShell.GetOrSetAsync(
        stringKey,
        expiry,
        async () =>
        {
            Interlocked.Increment(ref stringFactoryCalls);
            await Task.CompletedTask;
            return "value-from-factory";
        });

    var stringSecond = await cacheShell.GetOrSetAsync(
        stringKey,
        expiry,
        async () =>
        {
            Interlocked.Increment(ref stringFactoryCalls);
            await Task.CompletedTask;
            return "should-not-be-used";
        });

    var hashFactoryCalls = 0;
    var hashKey = $"{prefix}:hash";

    var hashFirst = await cacheShell.GetOrSetHashAsync(
        hashKey,
        "field1",
        expiry,
        async () =>
        {
            Interlocked.Increment(ref hashFactoryCalls);
            await Task.CompletedTask;
            return "hash-from-factory";
        });

    var hashSecond = await cacheShell.GetOrSetHashAsync(
        hashKey,
        "field1",
        expiry,
        async () =>
        {
            Interlocked.Increment(ref hashFactoryCalls);
            await Task.CompletedTask;
            return "should-not-be-used";
        });

    Console.WriteLine("[CacheShellTest] Start");
    Console.WriteLine(
        $"[CacheShellTest] String key: first={stringFirst}, second={stringSecond}, factoryInvocations={stringFactoryCalls} (expect 1)");
    Console.WriteLine(
        $"[CacheShellTest] Hash key/field: first={hashFirst}, second={hashSecond}, factoryInvocations={hashFactoryCalls} (expect 1)");
    Console.WriteLine("[CacheShellTest] End");
}