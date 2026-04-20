using System.Text.Json;
using System.Text;

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
    var runId = Guid.NewGuid().ToString("N");
    var sameSlotValues = BuildSameHashtagValues("FreeRedisTests", runId);
    var multiSlotValues = BuildMultiHashtagValues("FreeRedisTests", runId);

    await RunPipelineScenarioAsync(repository, "same-hashtag", sameSlotValues, expectSingleSlot: true);
    // await RunPipelineScenarioAsync(repository, "multi-hashtag", multiSlotValues, expectSingleSlot: false);
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

static async Task RunPipelineScenarioAsync(
    FreeRedisRepository repository,
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
