using System.Text.Json;
using CSRedis;

// Docker 无 TTY 时 stdout 常被全缓冲，导致 docker logs 长时间看不到输出；尽早打印并 Flush。
Console.WriteLine("[CsredisTests] Starting...");
Console.Out.Flush();

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
// --- 哨兵（Sentinel）连接相关：已全部注释，仅使用 ConnectionString 直连 ---
// var sentinelEnabled = redisSection.TryGetProperty("SentinelEnabled", out var sentinelEnabledElement) &&
//                       sentinelEnabledElement.GetBoolean();
// var sentinelServiceName = redisSection.TryGetProperty("SentinelServiceName", out var serviceNameElement)
//     ? serviceNameElement.GetString()
//     : null;
// var sentinelEndpoints = redisSection.TryGetProperty("SentinelEndpoints", out var endpointsElement) &&
//                         endpointsElement.ValueKind == JsonValueKind.Array
//     ? endpointsElement.EnumerateArray()
//         .Select(item => item.GetString())
//         .Where(item => !string.IsNullOrWhiteSpace(item))
//         .Cast<string>()
//         .ToArray()
//     : [];
// var sentinelHealthCheckEnabled = redisSection.TryGetProperty("SentinelHealthCheckEnabled", out var healthCheckEnabledElement)
//     ? healthCheckEnabledElement.GetBoolean()
//     : true;
// var sentinelHealthCheckIntervalSeconds = redisSection.TryGetProperty("SentinelHealthCheckIntervalSeconds", out var healthCheckIntervalElement)
//     ? healthCheckIntervalElement.GetInt32()
//     : 10;

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Redis:ConnectionString is not configured.");
    return;
}

Console.WriteLine("[CsredisTests] Creating Redis connection pool...");
Console.Out.Flush();

// using var redisPool = sentinelEnabled
//     ? new CsRedisConnectionPool(
//         $"{sentinelServiceName},connectTimeout=5000",
//         sentinelEndpoints,
//         poolSize,
//         readOnly: false)
//     : new CsRedisConnectionPool(connectionString!, poolSize);
using var redisPool = new CsRedisConnectionPool(connectionString!, poolSize);

RedisHelper.Initialization(redisPool.Client);
Console.WriteLine("[CsredisTests] RedisHelper initialized.");
Console.Out.Flush();

var repository = new CsRedisRepository(redisPool);
// using var sentinelManager = sentinelEnabled
//     ? new CsRedisSentinelManager(
//         sentinelEndpoints,
//         sentinelServiceName!,
//         redisPool,
//         message =>
//         {
//             Console.WriteLine($"[Sentinel] {message}");
//             Console.Out.Flush();
//         })
//     : null;
//
// if (sentinelManager is not null)
// {
//     Console.WriteLine("[CsredisTests] Connecting to Sentinel for pub/sub...");
//     Console.Out.Flush();
//     sentinelManager.MasterDownDetected += message => Console.WriteLine($"[Sentinel] Master down: {message}");
//     sentinelManager.MasterSwitched += message => Console.WriteLine($"[Sentinel] Master switched: {message}");
//     sentinelManager.StartAsync(
//         sentinelHealthCheckEnabled,
//         TimeSpan.FromSeconds(sentinelHealthCheckIntervalSeconds)).GetAwaiter().GetResult();
// }

Console.WriteLine("Redis connected (CSRedis). Sentinel: disabled in code.");
Console.Out.Flush();
Console.WriteLine($"RedisRepository ready with pool size: {poolSize}.");
Console.WriteLine("CacheShell (built-in RedisHelper.CacheShell) ready.");
Console.WriteLine("Tests will repeat every 15 seconds. Press Ctrl+C to stop.");
while (true)
{
    Console.WriteLine($"--- Test run @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
    RedisReachableNodesReport.Print(connectionString!);
    await RunPipelineTestAsync(repository);
    await RunCacheShellTestAsync();
    await Task.Delay(TimeSpan.FromSeconds(15));
}

static async Task RunPipelineTestAsync(CsRedisRepository repository)
{
    var prefix = $"pipeline:test:{Guid.NewGuid():N}";
    var values = new Dictionary<string, string>
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

static Task RunCacheShellTestAsync()
{
    var prefix = $"cacheshell:test:{Guid.NewGuid():N}";
    var expirySeconds = (int)TimeSpan.FromMinutes(5).TotalSeconds;

    var stringFactoryCalls = 0;
    var stringKey = $"{prefix}:string";

    var stringFirst = RedisHelper.CacheShell(stringKey, expirySeconds, () =>
    {
        Interlocked.Increment(ref stringFactoryCalls);
        return "value-from-factory";
    });

    var stringSecond = RedisHelper.CacheShell(stringKey, expirySeconds, () =>
    {
        Interlocked.Increment(ref stringFactoryCalls);
        return "should-not-be-used";
    });

    var hashFactoryCalls = 0;
    var hashKey = $"{prefix}:hash";

    var hashFirst = RedisHelper.CacheShell(hashKey, "field1", expirySeconds, () =>
    {
        Interlocked.Increment(ref hashFactoryCalls);
        return "hash-from-factory";
    });

    var hashSecond = RedisHelper.CacheShell(hashKey, "field1", expirySeconds, () =>
    {
        Interlocked.Increment(ref hashFactoryCalls);
        return "should-not-be-used";
    });

    Console.WriteLine("[CacheShellTest] Start");
    Console.WriteLine(
        $"[CacheShellTest] String key: first={stringFirst}, second={stringSecond}, factoryInvocations={stringFactoryCalls} (expect 1)");
    Console.WriteLine(
        $"[CacheShellTest] Hash key/field: first={hashFirst}, second={hashSecond}, factoryInvocations={hashFactoryCalls} (expect 1)");
    Console.WriteLine("[CacheShellTest] End");
    return Task.CompletedTask;
}
