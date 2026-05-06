using System.Text.Json;
using System.Text;
using CSRedis;
using CsredisTests;

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
var grpcEnabled = redisSection.TryGetProperty("GrpcEnabled", out var grpcEnabledElement)
    ? grpcEnabledElement.GetBoolean()
    : true;
var grpcPort = redisSection.TryGetProperty("GrpcPort", out var grpcPortElement)
    ? grpcPortElement.GetInt32()
    : 50051;
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
if (grpcEnabled)
{
    CsRedisGrpcHost.Start(repository, connectionString!, grpcPort);
    Console.WriteLine($"[CsredisTests] gRPC server listening on 0.0.0.0:{grpcPort} (service: RedisGetTest/*).");
    Console.Out.Flush();
}
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
// Console.WriteLine("Tests will repeat every 15 seconds. Press Ctrl+C to stop.");
Console.WriteLine($"--- Test run @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
RedisReachableNodesReport.Print(connectionString!);
await RunPipelineTestAsync(repository);
await RunCacheShellTestAsync();
while (true)
{

    // await Task.Delay(TimeSpan.FromSeconds(15));
    
    // break;
}

static async Task RunPipelineTestAsync(CsRedisRepository repository)
{
    var runId = Guid.NewGuid().ToString("N");
    var sameSlotValues = BuildSameHashtagValues("CsredisTests", runId);
    var multiSlotValues = BuildMultiHashtagValues("CsredisTests", runId);

    await RunPipelineScenarioAsync(repository, "same-hashtag", sameSlotValues, expectSingleSlot: true);
    await RunPipelineScenarioAsync(repository, "multi-hashtag", multiSlotValues, expectSingleSlot: false);
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

static async Task RunPipelineScenarioAsync(
    CsRedisRepository repository,
    string scenarioName,
    IReadOnlyDictionary<string, string> values,
    bool expectSingleSlot)
{
    var slots = values.Keys.Select(ComputeRedisClusterSlot).ToArray();
    var distinctSlots = slots.Distinct().OrderBy(x => x).ToArray();
    var slotExpectationMatched = expectSingleSlot ? distinctSlots.Length == 1 : distinctSlots.Length > 1;
    if (!slotExpectationMatched)
    {
        throw new InvalidOperationException(
            $"Scenario '{scenarioName}' slot distribution mismatch. Distinct slots: {string.Join(",", distinctSlots)}");
    }

    await repository.PipelineSetAsync(values, TimeSpan.FromMinutes(5));
    var readResult = await repository.PipelineGetStringAsync(values.Keys.ToList());

    Console.WriteLine($"[PipelineTest] Scenario={scenarioName}, DistinctSlots={distinctSlots.Length}, Slots=[{string.Join(",", distinctSlots)}]");
    var slotSummary = string.Join(", ", slots.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}->{g.Count()}"));
    Console.WriteLine($"[PipelineTest] SlotSummary={slotSummary}");
    foreach (var key in values.Keys)
    {
        Console.WriteLine($"[PipelineTest] {key} (slot={ComputeRedisClusterSlot(key)}) => {readResult[key]}");
    }
}

static Dictionary<string, string> BuildSameHashtagValues(string projectName, string runId)
{
    var hashTag = $"{projectName}:{runId}:same";
    return new Dictionary<string, string>
    {
        [$"pipeline:{projectName}:{{{hashTag}}}:k1"] = "v1",
        [$"pipeline:{projectName}:{{{hashTag}}}:k2"] = "v2",
        [$"pipeline:{projectName}:{{{hashTag}}}:k3"] = "v3"
    };
}

static Dictionary<string, string> BuildMultiHashtagValues(string projectName, string runId)
{
    var values = new Dictionary<string, string>(3);
    var usedSlots = new HashSet<int>();
    var keyIndex = 1;
    var salt = 0;
    while (values.Count < 3)
    {
        var hashTag = $"{projectName}:{runId}:multi:{keyIndex}:{salt}";
        var key = $"pipeline:{projectName}:{{{hashTag}}}:k{keyIndex}";
        var slot = ComputeRedisClusterSlot(key);
        if (usedSlots.Add(slot))
        {
            values[key] = $"v{keyIndex}";
            keyIndex++;
            continue;
        }

        salt++;
    }

    return values;
}

static int ComputeRedisClusterSlot(string key)
{
    var hashInput = ExtractHashTagOrKey(key);
    var bytes = Encoding.UTF8.GetBytes(hashInput);
    ushort crc = 0;
    foreach (var b in bytes)
    {
        crc ^= (ushort)(b << 8);
        for (var i = 0; i < 8; i++)
        {
            crc = (crc & 0x8000) != 0
                ? (ushort)((crc << 1) ^ 0x1021)
                : (ushort)(crc << 1);
        }
    }

    return crc % 16384;
}

static string ExtractHashTagOrKey(string key)
{
    var start = key.IndexOf('{');
    if (start >= 0)
    {
        var end = key.IndexOf('}', start + 1);
        if (end > start + 1)
        {
            return key.Substring(start + 1, end - start - 1);
        }
    }

    return key;
}
