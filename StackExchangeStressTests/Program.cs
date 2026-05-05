using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using StackExchange.Redis;

var options = ParseArgs(args);
Console.WriteLine(
    $"[SERedisStress] conn={options.ConnectionString}, method={options.Method}, total={options.TotalRequests}, concurrency={options.Concurrency}");

var redisOptions = ConfigurationOptions.Parse(options.ConnectionString);
redisOptions.AbortOnConnectFail = false;
using var mux = await ConnectionMultiplexer.ConnectAsync(redisOptions);
var db = mux.GetDatabase();

if (options.Seed)
{
    Console.WriteLine("[SERedisStress] seeding keys...");
    for (var i = 0; i < options.KeyCount; i++)
    {
        var key = $"{options.KeyPrefix}:{i}";
        await db.StringSetAsync(key, $"value-{i}", TimeSpan.FromMinutes(10));
    }
}

var overall = new StressStats(options.TotalRequests);
var methodStats = new ConcurrentDictionary<string, StressStats>(StringComparer.OrdinalIgnoreCase);
var requestCounter = 0;
var nodeProbeCache = new NodeProbeCache();
var random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
var globalSw = Stopwatch.StartNew();

var workers = Enumerable.Range(0, options.Concurrency)
    .Select(_ => Task.Run(async () =>
    {
        while (true)
        {
            var idx = Interlocked.Increment(ref requestCounter);
            if (idx > options.TotalRequests)
            {
                break;
            }

            var method = ResolveMethod(options.Method, idx);
            var sw = Stopwatch.StartNew();
            try
            {
                var ok = await ExecuteMethodAsync(method, idx);
                sw.Stop();
                overall.Add(sw.Elapsed.TotalMilliseconds, ok);
                methodStats.GetOrAdd(method, _ => new StressStats()).Add(sw.Elapsed.TotalMilliseconds, ok);
            }
            catch
            {
                sw.Stop();
                overall.Add(sw.Elapsed.TotalMilliseconds, false);
                methodStats.GetOrAdd(method, _ => new StressStats()).Add(sw.Elapsed.TotalMilliseconds, false);
            }
        }
    }))
    .ToArray();

await Task.WhenAll(workers);
globalSw.Stop();

overall.SortLatencies();
var elapsedSeconds = Math.Max(globalSw.Elapsed.TotalSeconds, 0.0001);
var qps = options.TotalRequests / elapsedSeconds;

Console.WriteLine("[SERedisStress] Completed");
Console.WriteLine(
    $"[SERedisStress] Overall Success={overall.SuccessCount}, Failed={overall.FailCount}, Duration={globalSw.Elapsed.TotalMilliseconds:F0}ms, QPS={qps:F2}");
Console.WriteLine(
    $"[SERedisStress] Overall Latency(ms): p50={Percentile(overall.LatencyMs, 0.50):F2}, p95={Percentile(overall.LatencyMs, 0.95):F2}, p99={Percentile(overall.LatencyMs, 0.99):F2}, max={Max(overall.LatencyMs):F2}");
foreach (var item in methodStats.OrderBy(x => x.Key))
{
    item.Value.SortLatencies();
    Console.WriteLine(
        $"[SERedisStress:{item.Key}] Success={item.Value.SuccessCount}, Failed={item.Value.FailCount}, p95={Percentile(item.Value.LatencyMs, 0.95):F2}ms, p99={Percentile(item.Value.LatencyMs, 0.99):F2}ms");
}

return;

async Task<bool> ExecuteMethodAsync(string method, int idx)
{
    switch (method)
    {
        case "get":
        {
            var keyIndex = random.Value!.Next(options.KeyCount);
            var key = $"{options.KeyPrefix}:{keyIndex}";
            _ = await db.StringGetAsync(key);
            return true;
        }
        case "pipeline":
        {
            var runId = $"{idx}:{Environment.CurrentManagedThreadId}";
            var values = BuildPipelineValues(options.KeyPrefix, runId, options.PipelineKeyCount, options.SameHashtag);
            var batch = db.CreateBatch();
            var setTasks = values
                .Select(item => batch.StringSetAsync(item.Key, item.Value, TimeSpan.FromSeconds(options.ExpirySeconds)))
                .ToArray();
            batch.Execute();
            await Task.WhenAll(setTasks);

            var readBatch = db.CreateBatch();
            var readTasks = values.Keys.Select(key => readBatch.StringGetAsync(key)).ToArray();
            readBatch.Execute();
            await Task.WhenAll(readTasks);
            return readTasks.All(x => x.Result.HasValue);
        }
        case "cacheshell":
        {
            var prefix = $"{options.KeyPrefix}:cacheshell:{idx}";
            var expiry = TimeSpan.FromSeconds(options.ExpirySeconds);
            var stringFactoryCalls = 0;
            var hashFactoryCalls = 0;
            var stringKey = $"{prefix}:string";
            var hashKey = $"{prefix}:hash";

            _ = await GetOrSetStringAsync(
                stringKey,
                expiry,
                async () =>
                {
                    Interlocked.Increment(ref stringFactoryCalls);
                    await Task.CompletedTask;
                    return "value-from-factory";
                });
            _ = await GetOrSetStringAsync(
                stringKey,
                expiry,
                async () =>
                {
                    Interlocked.Increment(ref stringFactoryCalls);
                    await Task.CompletedTask;
                    return "should-not-be-used";
                });

            _ = await GetOrSetHashAsync(
                hashKey,
                options.HashField,
                expiry,
                async () =>
                {
                    Interlocked.Increment(ref hashFactoryCalls);
                    await Task.CompletedTask;
                    return "hash-from-factory";
                });
            _ = await GetOrSetHashAsync(
                hashKey,
                options.HashField,
                expiry,
                async () =>
                {
                    Interlocked.Increment(ref hashFactoryCalls);
                    await Task.CompletedTask;
                    return "should-not-be-used";
                });

            return stringFactoryCalls == 1 && hashFactoryCalls == 1;
        }
        case "slot":
        {
            var k1 = $"{options.KeyPrefix}:slot:{{u:{idx}}}:k1";
            var k2 = $"{options.KeyPrefix}:slot:{{u:{idx}}}:k2";
            var k3 = $"{options.KeyPrefix}:slot:{idx}:k3";
            _ = ComputeRedisClusterSlot(k1);
            _ = ComputeRedisClusterSlot(k2);
            _ = ComputeRedisClusterSlot(k3);
            return true;
        }
        case "node":
            return await nodeProbeCache.ProbeAsync(mux);
        default:
            throw new ArgumentOutOfRangeException(nameof(method), method, "unsupported method");
    }
}

async Task<string> GetOrSetStringAsync(string key, TimeSpan expiry, Func<Task<string>> factory)
{
    var existing = await db.StringGetAsync(key);
    if (existing.HasValue)
    {
        return existing.ToString();
    }

    var lockKey = $"{key}:lock";
    var token = Guid.NewGuid().ToString("N");
    var lockAcquired = await db.StringSetAsync(lockKey, token, TimeSpan.FromSeconds(5), when: When.NotExists);
    if (lockAcquired)
    {
        try
        {
            existing = await db.StringGetAsync(key);
            if (existing.HasValue)
            {
                return existing.ToString();
            }

            var value = await factory();
            await db.StringSetAsync(key, value, expiry);
            return value;
        }
        finally
        {
            const string releaseScript = """
                                         if redis.call('GET', KEYS[1]) == ARGV[1] then
                                           return redis.call('DEL', KEYS[1])
                                         end
                                         return 0
                                         """;
            await db.ScriptEvaluateAsync(releaseScript, [lockKey], [token]);
        }
    }

    for (var i = 0; i < 5; i++)
    {
        await Task.Delay(20 + Random.Shared.Next(20));
        existing = await db.StringGetAsync(key);
        if (existing.HasValue)
        {
            return existing.ToString();
        }
    }

    return await factory();
}

async Task<string> GetOrSetHashAsync(string key, string field, TimeSpan expiry, Func<Task<string>> factory)
{
    var existing = await db.HashGetAsync(key, field);
    if (existing.HasValue)
    {
        return existing.ToString();
    }

    var lockKey = $"{key}:{field}:lock";
    var token = Guid.NewGuid().ToString("N");
    var lockAcquired = await db.StringSetAsync(lockKey, token, TimeSpan.FromSeconds(5), when: When.NotExists);
    if (lockAcquired)
    {
        try
        {
            existing = await db.HashGetAsync(key, field);
            if (existing.HasValue)
            {
                return existing.ToString();
            }

            var value = await factory();
            await db.HashSetAsync(key, field, value);
            await db.KeyExpireAsync(key, expiry);
            return value;
        }
        finally
        {
            const string releaseScript = """
                                         if redis.call('GET', KEYS[1]) == ARGV[1] then
                                           return redis.call('DEL', KEYS[1])
                                         end
                                         return 0
                                         """;
            await db.ScriptEvaluateAsync(releaseScript, [lockKey], [token]);
        }
    }

    for (var i = 0; i < 5; i++)
    {
        await Task.Delay(20 + Random.Shared.Next(20));
        existing = await db.HashGetAsync(key, field);
        if (existing.HasValue)
        {
            return existing.ToString();
        }
    }

    return await factory();
}

static Dictionary<string, RedisValue> BuildPipelineValues(string keyPrefix, string runId, int keyCount, bool sameHashtag)
{
    if (sameHashtag)
    {
        var hashTag = $"same:{runId}";
        return Enumerable.Range(1, keyCount)
            .ToDictionary(
                idx => $"{keyPrefix}:pipeline:{{{hashTag}}}:k{idx}",
                idx => (RedisValue)$"v{idx}");
    }

    var values = new Dictionary<string, RedisValue>(keyCount);
    var usedSlots = new HashSet<int>();
    var index = 1;
    var salt = 0;
    while (values.Count < keyCount)
    {
        var hashTag = $"multi:{runId}:{index}:{salt}";
        var key = $"{keyPrefix}:pipeline:{{{hashTag}}}:k{index}";
        var slot = ComputeRedisClusterSlot(key);
        if (usedSlots.Add(slot))
        {
            values[key] = $"v{index}";
            index++;
        }
        else
        {
            salt++;
        }
    }

    return values;
}

static string ResolveMethod(string configured, int idx)
{
    if (!string.Equals(configured, "all", StringComparison.OrdinalIgnoreCase))
    {
        return configured;
    }

    return (idx % 5) switch
    {
        0 => "get",
        1 => "pipeline",
        2 => "cacheshell",
        3 => "slot",
        _ => "node"
    };
}

static double Percentile(List<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
    index = Math.Clamp(index, 0, sortedValues.Count - 1);
    return sortedValues[index];
}

static double Max(List<double> sortedValues) => sortedValues.Count == 0 ? 0 : sortedValues[^1];

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

static StressOptions ParseArgs(string[] args)
{
    var connectionString = "redis-cluster-1:6379,redis-cluster-2:6379,redis-cluster-3:6379,abortConnect=false,connectTimeout=5000";
    var total = 10000;
    var concurrency = 100;
    var keyPrefix = "se:stress:key";
    var keyCount = 2000;
    var seed = true;
    var method = "all";
    var pipelineKeyCount = 3;
    var expirySeconds = 300;
    var sameHashtag = true;
    var hashField = "field1";

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--conn":
                connectionString = NextArg(args, ref i, "--conn");
                break;
            case "--total":
                total = int.Parse(NextArg(args, ref i, "--total"));
                break;
            case "--concurrency":
                concurrency = int.Parse(NextArg(args, ref i, "--concurrency"));
                break;
            case "--key-prefix":
                keyPrefix = NextArg(args, ref i, "--key-prefix");
                break;
            case "--key-count":
                keyCount = int.Parse(NextArg(args, ref i, "--key-count"));
                break;
            case "--method":
                method = NextArg(args, ref i, "--method").ToLowerInvariant();
                break;
            case "--pipeline-key-count":
                pipelineKeyCount = int.Parse(NextArg(args, ref i, "--pipeline-key-count"));
                break;
            case "--expiry-seconds":
                expirySeconds = int.Parse(NextArg(args, ref i, "--expiry-seconds"));
                break;
            case "--hash-field":
                hashField = NextArg(args, ref i, "--hash-field");
                break;
            case "--multi-hashtag":
                sameHashtag = false;
                break;
            case "--no-seed":
                seed = false;
                break;
            case "--help":
            case "-h":
                PrintHelp();
                Environment.Exit(0);
                break;
        }
    }

    if (total <= 0 || concurrency <= 0 || keyCount <= 0 || pipelineKeyCount <= 0 || expirySeconds <= 0)
    {
        throw new ArgumentOutOfRangeException("total/concurrency/key-count/pipeline-key-count/expiry-seconds must be > 0");
    }

    if (method is not ("get" or "pipeline" or "cacheshell" or "slot" or "node" or "all"))
    {
        throw new ArgumentOutOfRangeException(nameof(method), "method must be get/pipeline/cacheshell/slot/node/all");
    }

    return new StressOptions(
        connectionString,
        total,
        concurrency,
        keyPrefix,
        keyCount,
        seed,
        method,
        pipelineKeyCount,
        expirySeconds,
        sameHashtag,
        hashField);
}

static string NextArg(string[] args, ref int i, string name)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"{name} requires value");
    }

    i++;
    return args[i];
}

static void PrintHelp()
{
    Console.WriteLine("Usage: dotnet run --project StackExchangeStressTests -- [options]");
    Console.WriteLine("  --conn <connectionString>                  redis connection string");
    Console.WriteLine("  --method <get|pipeline|cacheshell|slot|node|all>  default: all");
    Console.WriteLine("  --total <n>                                total request count");
    Console.WriteLine("  --concurrency <n>                          concurrent worker count");
    Console.WriteLine("  --key-prefix <prefix>                      key prefix");
    Console.WriteLine("  --key-count <n>                            key cardinality for random GET");
    Console.WriteLine("  --pipeline-key-count <n>                   pipeline key count, default: 3");
    Console.WriteLine("  --expiry-seconds <n>                       ttl for pipeline/cacheshell, default: 300");
    Console.WriteLine("  --hash-field <name>                        hash field for cacheshell, default: field1");
    Console.WriteLine("  --multi-hashtag                            pipeline uses multi-slot key pattern");
    Console.WriteLine("  --no-seed                                  skip SET seeding stage");
}

internal sealed class StressStats
{
    private int _successCount;
    private int _failCount;

    public StressStats(int capacity = 0)
    {
        LatencyMs = capacity > 0 ? new List<double>(capacity) : [];
    }

    public List<double> LatencyMs { get; }

    public int SuccessCount => _successCount;

    public int FailCount => _failCount;

    public void Add(double latencyMs, bool ok)
    {
        lock (LatencyMs)
        {
            LatencyMs.Add(latencyMs);
        }

        if (ok)
        {
            Interlocked.Increment(ref _successCount);
        }
        else
        {
            Interlocked.Increment(ref _failCount);
        }
    }

    public void SortLatencies()
    {
        lock (LatencyMs)
        {
            LatencyMs.Sort();
        }
    }
}

internal sealed class NodeProbeCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private bool _lastResult;

    public async Task<bool> ProbeAsync(ConnectionMultiplexer mux)
    {
        var now = DateTimeOffset.UtcNow;
        if (_expiresAt > now)
        {
            return _lastResult;
        }

        await _lock.WaitAsync();
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_expiresAt > now)
            {
                return _lastResult;
            }

            var ok = false;
            foreach (var endpoint in mux.GetEndPoints())
            {
                try
                {
                    var server = mux.GetServer(endpoint);
                    if (!server.IsConnected)
                    {
                        continue;
                    }

                    _ = await server.PingAsync();
                    ok = true;
                    break;
                }
                catch
                {
                    // Continue probing next endpoint.
                }
            }

            _lastResult = ok;
            _expiresAt = DateTimeOffset.UtcNow.Add(Ttl);
            return ok;
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed record StressOptions(
    string ConnectionString,
    int TotalRequests,
    int Concurrency,
    string KeyPrefix,
    int KeyCount,
    bool Seed,
    string Method,
    int PipelineKeyCount,
    int ExpirySeconds,
    bool SameHashtag,
    string HashField);
