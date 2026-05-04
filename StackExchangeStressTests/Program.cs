using System.Diagnostics;
using StackExchange.Redis;

var options = ParseArgs(args);
Console.WriteLine(
    $"[SERedisStress] conn={options.ConnectionString}, total={options.TotalRequests}, concurrency={options.Concurrency}, keyCount={options.KeyCount}");

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

var latencies = new List<double>(options.TotalRequests);
var success = 0;
var fail = 0;
var counter = 0;
var rand = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
var globalSw = Stopwatch.StartNew();

var workers = Enumerable.Range(0, options.Concurrency)
    .Select(workerId => Task.Run(async () =>
    {
        while (true)
        {
            var idx = Interlocked.Increment(ref counter);
            if (idx > options.TotalRequests)
            {
                break;
            }

            var keyIndex = rand.Value!.Next(options.KeyCount);
            var key = $"{options.KeyPrefix}:{keyIndex}";
            var sw = Stopwatch.StartNew();
            try
            {
                var _ = await db.StringGetAsync(key);
                sw.Stop();
                lock (latencies)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
                Interlocked.Increment(ref success);
            }
            catch
            {
                sw.Stop();
                lock (latencies)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
                Interlocked.Increment(ref fail);
            }
        }
    }))
    .ToArray();

await Task.WhenAll(workers);
globalSw.Stop();

latencies.Sort();
var elapsedSeconds = Math.Max(globalSw.Elapsed.TotalSeconds, 0.0001);
var qps = options.TotalRequests / elapsedSeconds;

Console.WriteLine("[SERedisStress] Completed");
Console.WriteLine($"[SERedisStress] Success={success}, Failed={fail}, Duration={globalSw.Elapsed.TotalMilliseconds:F0}ms, QPS={qps:F2}");
Console.WriteLine(
    $"[SERedisStress] Latency(ms): p50={Percentile(latencies, 0.50):F2}, p95={Percentile(latencies, 0.95):F2}, p99={Percentile(latencies, 0.99):F2}, max={(latencies.Count == 0 ? 0 : latencies[^1]):F2}");

static double Percentile(List<double> sorted, double p)
{
    if (sorted.Count == 0)
    {
        return 0;
    }

    var idx = (int)Math.Ceiling(sorted.Count * p) - 1;
    idx = Math.Clamp(idx, 0, sorted.Count - 1);
    return sorted[idx];
}

static StressOptions ParseArgs(string[] args)
{
    var connectionString = "redis-cluster-1:6379,redis-cluster-2:6379,redis-cluster-3:6379,abortConnect=false,connectTimeout=5000";
    var total = 50000;
    var concurrency = 200;
    var keyPrefix = "se:stress:key";
    var keyCount = 2000;
    var seed = true;

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

    if (total <= 0 || concurrency <= 0 || keyCount <= 0)
    {
        throw new ArgumentOutOfRangeException("total/concurrency/key-count must be > 0");
    }

    return new StressOptions(connectionString, total, concurrency, keyPrefix, keyCount, seed);
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
    Console.WriteLine("  --conn <connectionString>  redis connection string");
    Console.WriteLine("  --total <n>                total request count");
    Console.WriteLine("  --concurrency <n>          concurrent worker count");
    Console.WriteLine("  --key-prefix <prefix>      key prefix");
    Console.WriteLine("  --key-count <n>            key cardinality for random GET");
    Console.WriteLine("  --no-seed                  skip SET seeding stage");
}

internal sealed record StressOptions(
    string ConnectionString,
    int TotalRequests,
    int Concurrency,
    string KeyPrefix,
    int KeyCount,
    bool Seed);
