using System.Text.Json;

Console.WriteLine("[FreeRedisTests] Starting...");
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

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Redis:ConnectionString is not configured.");
    return;
}

Console.WriteLine("[FreeRedisTests] Creating Redis connection pool...");
Console.Out.Flush();

using var redisPool = new FreeRedisConnectionPool(connectionString!, poolSize);
var repository = new FreeRedisRepository(redisPool);
var cacheShell = new FreeRedisCacheShell(repository, redisPool, message => Console.WriteLine($"[CacheShell] {message}"));

Console.WriteLine("Redis connected (FreeRedis).");
Console.Out.Flush();
Console.WriteLine($"RedisRepository ready with pool size: {poolSize}.");
Console.WriteLine($"CacheShell ready: {cacheShell.GetType().Name}.");
Console.WriteLine("Tests will repeat every 15 seconds. Press Ctrl+C to stop.");
while (true)
{
    Console.WriteLine($"--- Test run @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
    RedisReachableNodesReport.Print(connectionString!);
    await RunPipelineTestAsync(repository);
    await RunCacheShellTestAsync(cacheShell);
    await Task.Delay(TimeSpan.FromSeconds(15));
}

static async Task RunPipelineTestAsync(FreeRedisRepository repository)
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

static async Task RunCacheShellTestAsync(FreeRedisCacheShell cacheShell)
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
